using System.Text.Json.Nodes;
using Station.Abstractions;

namespace Station.Core;

public sealed class ConfigEngine
{
    private readonly IReadOnlyList<ModuleRegistration> builtInModules;
    private readonly ConfigLoader configLoader;
    private readonly PluginRestorer pluginRestorer;
    private readonly IPrivilegeDetector privilegeDetector;

    public ConfigEngine()
        : this(
            new ConfigLoader(),
            new PluginRestorer(new ConfigLoader(), new ProcessRunner()),
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
        var privilegedTasks = new List<string>();
        var isElevated = privilegeDetector.IsElevated();

        foreach (var task in tasks)
        {
            ResolvedModule resolvedModule;
            try
            {
                resolvedModule = moduleCatalog.Resolve(task.ModuleName);
            }
            catch (ConfigurationException exception)
            {
                diagnostics.Add($"{task.Name}: {exception.Message}");
                continue;
            }

            var request = CreateRequest(mode, configuration, task, isElevated);
            log?.Invoke($"Inspecting {task.Name} [{resolvedModule.FullName}]");

            ModuleInspectionResult inspection;
            try
            {
                inspection = await resolvedModule.Module.InspectAsync(request, cancellationToken);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"{task.Name}: inspection failed: {exception.Message}");
                continue;
            }

            foreach (var diagnostic in inspection.Diagnostics)
            {
                var prefix = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Warning => "warning",
                    DiagnosticSeverity.Info => "info",
                    _ => "error",
                };

                diagnostics.Add($"{task.Name}: {prefix}: {diagnostic.Message}");
            }

            if (inspection.RequiredPrivilege == PrivilegeRequirement.Administrator && !isElevated)
            {
                privilegedTasks.Add(task.Name);
            }
        }

        if (diagnostics.Count > 0)
        {
            return new EngineRunResult(
                mode,
                false,
                "Validation failed.",
                diagnostics,
                []);
        }

        if (privilegedTasks.Count > 0)
        {
            var message =
                $"The following tasks require elevation: {string.Join(", ", privilegedTasks)}. Rerun the command from an elevated terminal.";
            return new EngineRunResult(mode, false, message, [message], []);
        }

        if (mode == ExecutionMode.Validate)
        {
            return new EngineRunResult(
                mode,
                true,
                $"Validated {tasks.Count} task(s).",
                [],
                []);
        }

        var records = new List<EngineTaskRecord>();
        foreach (var task in tasks)
        {
            var resolvedModule = moduleCatalog.Resolve(task.ModuleName);
            var request = CreateRequest(mode, configuration, task, isElevated);
            log?.Invoke($"Running {task.Name} [{resolvedModule.FullName}]");

            ModuleResult result;
            try
            {
                result = await resolvedModule.Module.ExecuteAsync(request, cancellationToken);
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

        var successMessage = mode switch
        {
            ExecutionMode.Check => $"Check completed. {records.Count(static record => record.Result.Changed)} task(s) would change.",
            ExecutionMode.Apply => $"Apply completed. {records.Count(static record => record.Result.Changed)} task(s) changed.",
            _ => $"Completed {records.Count} task(s).",
        };

        return new EngineRunResult(mode, true, successMessage, [], records);
    }

    private static ModuleExecutionRequest CreateRequest(
        ExecutionMode mode,
        LoadedConfiguration configuration,
        LoadedTask task,
        bool isElevated) =>
        new(
            mode,
            task.Name,
            configuration.ConfigRoot,
            task.TaskRoot,
            task.TaskRoot,
            task.RoleName,
            task.RoleRoot,
            (JsonObject)task.Parameters.DeepClone(),
            task.Variables,
            isElevated);
}
