using System.Text.Json.Nodes;
using System.Text.Json;

using Station.Abstractions.Enums;
using Station.Abstractions.Models;
using Station.Core.Configuration;
using Station.Core.Infrastructure.Privileges;
using Station.Core.Infrastructure.Processes;
using Station.Core.Infrastructure.Templates;
using Station.Core.Plugins;

namespace Station.Core.Execution;

public sealed class ConfigEngine
{
    private readonly IReadOnlyList<ModuleRegistration> builtInModules;
    private readonly ModuleBinder moduleBinder = new();
    private readonly ConfigLoader configLoader;
    private readonly PluginRestorer pluginRestorer;
    private readonly IPrivilegeDetector privilegeDetector;
    private readonly ScribanTemplateRenderer templateEngine = new();

    public ConfigEngine()
        : this(
            new ConfigLoader(new ScribanTemplateRenderer()),
            new PluginRestorer(new ConfigLoader(new ScribanTemplateRenderer()), new ProcessRunner()),
            new WindowsPrivilegeDetector(),
            [])
    {
    }

    internal ConfigEngine(
        ConfigLoader configLoader,
        PluginRestorer pluginRestorer,
        IPrivilegeDetector privilegeDetector,
        IEnumerable<ModuleRegistration> builtInModules)
    {
        this.configLoader = configLoader;
        this.pluginRestorer = pluginRestorer;
        this.privilegeDetector = privilegeDetector;
        this.builtInModules = builtInModules.ToArray();
    }

    public async Task<PluginRestoreResult> RestoreAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        LoadedConfiguration configuration;
        try
        {
            configuration = configLoader.Load(configRoot, cliVariables);
        }
        catch (Exception exception) when (exception is ConfigurationException or IOException)
        {
            return new PluginRestoreResult(false, exception.Message, [exception.Message], [], []);
        }

        return await pluginRestorer.RestoreAsync(
            configuration.ConfigRoot,
            configuration.PluginDeclarations,
            log,
            cancellationToken);
    }

    public async Task<EngineRunResult> RunAsync(
        string configRoot,
        ExecutionMode mode,
        IReadOnlyDictionary<string, string>? cliVariables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (mode == ExecutionMode.Restore)
        {
            throw new InvalidOperationException("Use RestoreAsync for restore operations.");
        }

        LoadedConfiguration configuration;
        PluginRestoreResult restoreResult;
        ModuleCatalog moduleCatalog;
        IReadOnlyList<LoadedTask> tasks;

        try
        {
            configuration = configLoader.Load(configRoot, cliVariables);
            restoreResult = await pluginRestorer.RestoreAsync(
                configuration.ConfigRoot,
                configuration.PluginDeclarations,
                log,
                cancellationToken);

            if (!restoreResult.Succeeded)
            {
                return new EngineRunResult(mode, false, restoreResult.Message, restoreResult.Diagnostics, []);
            }

            moduleCatalog = ModuleCatalog.Create(
                builtInModules,
                configLoader.DiscoverLocalModulePaths(configuration.ConfigRoot),
                restoreResult.RestoredPlugins);

            var roleCatalog = RoleCatalog.Create(
                configLoader.DiscoverLocalRoles(configuration.ConfigRoot),
                restoreResult.RestoredPlugins);

            var resolvedRoles = configuration.RequestedRoles
                .Select(roleCatalog.Resolve)
                .ToArray();
            var roleTasks = resolvedRoles
                .SelectMany(role => configLoader.LoadRoleTasks(role, configuration.RootVariables))
                .ToArray();

            tasks = configuration.RootTasks.Concat(roleTasks).ToArray();
        }
        catch (Exception exception) when (exception is ConfigurationException or IOException)
        {
            return new EngineRunResult(mode, false, exception.Message, [exception.Message], []);
        }

        var diagnostics = new List<string>();
        var isElevated = privilegeDetector.IsElevated();
        var records = new List<EngineTaskRecord>();

        foreach (var task in tasks)
        {
            ResolvedModule resolvedModule;
            try
            {
                resolvedModule = moduleCatalog.Resolve(task.ModuleName);
            }
            catch (ConfigurationException exception)
            {
                diagnostics.Add($"{task.Name}: error: {exception.Message}");
                continue;
            }

            var context = new ModuleContext(
                mode,
                task.Name,
                configuration.ConfigRoot,
                task.TaskRoot,
                task.RoleName,
                task.RoleRoot,
                task.Variables,
                ModuleArtifactFactory.Create(configuration.ConfigRoot, task.RoleRoot, task.Variables, templateEngine),
                templateEngine,
                isElevated,
                TimeProvider.System
            );
            var binding = moduleBinder.Bind(
                resolvedModule.Schema,
                context,
                (JsonObject)task.Parameters.DeepClone(),
                allowDeferred: mode == ExecutionMode.Validate
            );

            foreach (var diagnostic in binding.Diagnostics)
            {
                diagnostics.Add($"{task.Name}: {FormatDiagnostic(diagnostic)}");
            }

            if (binding.HasErrors)
            {
                if (mode == ExecutionMode.Validate)
                {
                    continue;
                }

                return new EngineRunResult(mode, false, "Validation failed.", diagnostics, records);
            }

            if (binding.IsDeferred)
            {
                continue;
            }

            log?.Invoke($"Inspecting {task.Name} [{resolvedModule.FullName}]");

            ModuleInspectionResult inspection;
            try
            {
                inspection = await binding.Module!.InspectAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"{task.Name}: inspection failed: {exception.Message}");
                if (mode == ExecutionMode.Validate)
                {
                    continue;
                }

                return new EngineRunResult(mode, false, "Validation failed.", diagnostics, records);
            }

            foreach (var diagnostic in inspection.Diagnostics)
            {
                diagnostics.Add($"{task.Name}: {FormatDiagnostic(diagnostic)}");
            }

            if (inspection.RequiredPrivilege == PrivilegeRequirement.Administrator && !isElevated)
            {
                var message = $"The task '{task.Name}' requires elevation. Rerun the command from an elevated terminal.";
                diagnostics.Add($"{task.Name}: error: {message}");
                if (mode == ExecutionMode.Validate)
                {
                    continue;
                }

                return new EngineRunResult(mode, false, message, diagnostics, records);
            }

            if (mode == ExecutionMode.Validate)
            {
                continue;
            }

            log?.Invoke($"Running {task.Name} [{resolvedModule.FullName}]");

            ModuleResult result;
            try
            {
                result = await binding.Module!.ExecuteAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                var message = $"{task.Name}: execution failed: {exception.Message}";
                records.Add(new EngineTaskRecord(task.Name, resolvedModule.FullName, task.RoleName, ModuleResult.Failure(message)));
                return new EngineRunResult(mode, false, message, [message], records);
            }

            records.Add(new EngineTaskRecord(task.Name, resolvedModule.FullName, task.RoleName, result));

            if (result.Failed)
            {
                return new EngineRunResult(
                    mode,
                    false,
                    result.Message ?? $"{task.Name} failed.",
                    [result.Message ?? $"{task.Name} failed."],
                    records);
            }
        }

        if (mode == ExecutionMode.Validate)
        {
            return diagnostics.Any(static item => item.Contains(": error:", StringComparison.OrdinalIgnoreCase))
                ? new EngineRunResult(mode, false, "Validation failed.", diagnostics, [])
                : new EngineRunResult(mode, true, $"Validated {tasks.Count} task(s).", diagnostics, []);
        }

        var successMessage = mode switch
        {
            ExecutionMode.Check => $"Check completed. {records.Count(static record => record.Result.Changed)} task(s) would change.",
            ExecutionMode.Apply => $"Apply completed. {records.Count(static record => record.Result.Changed)} task(s) changed.",
            _ => $"Completed {records.Count} task(s).",
        };

        return new EngineRunResult(mode, true, successMessage, [], records);
    }

    public async Task<ExplainResult> ExplainAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        LoadedConfiguration configuration;
        PluginRestoreResult restoreResult;
        ModuleCatalog moduleCatalog;
        IReadOnlyList<LoadedTask> tasks;

        try
        {
            configuration = configLoader.Load(configRoot, cliVariables);
            restoreResult = await pluginRestorer.RestoreAsync(
                configuration.ConfigRoot,
                configuration.PluginDeclarations,
                log,
                cancellationToken);

            if (!restoreResult.Succeeded)
            {
                return new ExplainResult(false, restoreResult.Message, restoreResult.Diagnostics, string.Empty);
            }

            moduleCatalog = ModuleCatalog.Create(
                builtInModules,
                configLoader.DiscoverLocalModulePaths(configuration.ConfigRoot),
                restoreResult.RestoredPlugins);

            var roleCatalog = RoleCatalog.Create(
                configLoader.DiscoverLocalRoles(configuration.ConfigRoot),
                restoreResult.RestoredPlugins);

            var resolvedRoles = configuration.RequestedRoles
                .Select(roleCatalog.Resolve)
                .ToArray();
            var roleTasks = resolvedRoles
                .SelectMany(role => configLoader.LoadRoleTasks(role, configuration.RootVariables))
                .ToArray();

            tasks = configuration.RootTasks.Concat(roleTasks).ToArray();
        }
        catch (Exception exception) when (exception is ConfigurationException or IOException)
        {
            return new ExplainResult(false, exception.Message, [exception.Message], string.Empty);
        }

        var diagnostics = new List<string>();
        var lines = new List<string> { "station" };
        AppendSection(lines, "variables", configuration.RootVariables);

        if (tasks.Count == 0)
        {
            lines.Add("+- tasks");
            lines.Add("|  \\- <none>");
        }
        else
        {
            lines.Add("+- tasks");
            for (var index = 0; index < tasks.Count; index++)
            {
                var task = tasks[index];
                var taskPrefix = index == tasks.Count - 1 ? "   " : "|  ";
                ResolvedModule? resolvedModule = null;
                try
                {
                    resolvedModule = moduleCatalog.Resolve(task.ModuleName);
                }
                catch (ConfigurationException exception)
                {
                    diagnostics.Add($"{task.Name}: error: {exception.Message}");
                    lines.Add($"{taskPrefix}+- {task.Name} [{task.ModuleName}]");
                    lines.Add($"{taskPrefix}|  +- error: {exception.Message}");
                    continue;
                }

                var context = new ModuleContext(
                    ExecutionMode.Validate,
                    task.Name,
                    configuration.ConfigRoot,
                    task.TaskRoot,
                    task.RoleName,
                    task.RoleRoot,
                    task.Variables,
                    ModuleArtifactFactory.Create(configuration.ConfigRoot, task.RoleRoot, task.Variables, templateEngine),
                    templateEngine,
                    isElevated: false,
                    TimeProvider.System
                );
                var binding = moduleBinder.Bind(
                    resolvedModule.Schema,
                    context,
                    (JsonObject)task.Parameters.DeepClone(),
                    allowDeferred: true
                );

                foreach (var diagnostic in binding.Diagnostics)
                {
                    diagnostics.Add($"{task.Name}: {FormatDiagnostic(diagnostic)}");
                }

                lines.Add($"{taskPrefix}+- {task.Name} [{resolvedModule.FullName}]");
                lines.Add($"{taskPrefix}|  +- role: {task.RoleName ?? "root"}");
                lines.Add($"{taskPrefix}|  +- parameters");
                if (binding.Parameters.Count == 0)
                {
                    lines.Add($"{taskPrefix}|  |  \\- <none>");
                }
                else
                {
                    foreach (var parameter in binding.Parameters)
                    {
                        var suffix = parameter.IsDeferred ? " (deferred)" : parameter.IsDefaultValue ? " (default)" : string.Empty;
                        lines.Add(
                            $"{taskPrefix}|  |  +- {parameter.Name} = {parameter.DisplayValue}{suffix}"
                        );
                    }
                }

                lines.Add($"{taskPrefix}|  +- variables");
                var variablePaths = task.Variables.EnumeratePaths().OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                if (variablePaths.Length == 0)
                {
                    lines.Add($"{taskPrefix}|  |  \\- <none>");
                }
                else
                {
                    foreach (var variable in variablePaths)
                    {
                        lines.Add($"{taskPrefix}|  |  +- {variable.Path} = {FormatNode(variable.Value)}");
                    }
                }
            }
        }

        var succeeded = diagnostics.All(static diagnostic =>
            !diagnostic.Contains(": error:", StringComparison.OrdinalIgnoreCase));
        return new ExplainResult(
            succeeded,
            $"Explained {tasks.Count} task(s).",
            diagnostics,
            string.Join(Environment.NewLine, lines)
        );
    }

    private static string FormatDiagnostic(ModuleDiagnostic diagnostic)
    {
        var prefix = diagnostic.Severity switch
        {
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            _ => "error",
        };

        return $"{prefix}: {diagnostic.Message}";
    }

    private static void AppendSection(List<string> lines, string title, VariableSet variables)
    {
        lines.Add($"+- {title}");
        var values = variables.EnumeratePaths().OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0)
        {
            lines.Add("|  \\- <none>");
            return;
        }

        foreach (var value in values)
        {
            lines.Add($"|  +- {value.Path} = {FormatNode(value.Value)}");
        }
    }

    private static string FormatNode(JsonNode? node) =>
        node switch
        {
            null => "<null>",
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue) => stringValue,
            _ => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
        };
}
