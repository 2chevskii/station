using Station.Abstractions.Execution;
using Station.Core.Configuration.Loading;
using Station.Core.Execution.Modules;
using Station.Core.Execution.Privileges;

namespace Station.Core.Execution.Running;

internal sealed class TaskExecutionPipeline(
    ModuleBinder moduleBinder,
    IPrivilegeDetector privilegeDetector,
    ScribanTemplateRenderer templateEngine
)
{
    public async Task<EngineRunResult> RunAsync(
        LoadedConfiguration configuration,
        ModuleCatalog moduleCatalog,
        IReadOnlyList<LoadedTask> tasks,
        ExecutionMode mode,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = new List<string>();
        var hasErrors = false;
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
                hasErrors = true;
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
                ModuleArtifactFactory.Create(
                    configuration.ConfigRoot,
                    task.RoleRoot,
                    task.Variables,
                    templateEngine
                ),
                templateEngine,
                isElevated,
                TimeProvider.System
            );
            var binding = moduleBinder.Bind(
                resolvedModule.Schema,
                context,
                task.Parameters,
                allowDeferred: mode == ExecutionMode.Validate
            );

            foreach (var diagnostic in binding.Diagnostics)
            {
                diagnostics.Add($"{task.Name}: {ModuleDiagnosticFormatter.Format(diagnostic)}");
            }

            if (binding.HasErrors)
            {
                hasErrors = true;
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
                hasErrors = true;
                diagnostics.Add($"{task.Name}: inspection failed: {exception.Message}");
                if (mode == ExecutionMode.Validate)
                {
                    continue;
                }

                return new EngineRunResult(mode, false, "Validation failed.", diagnostics, records);
            }

            foreach (var diagnostic in inspection.Diagnostics)
            {
                diagnostics.Add($"{task.Name}: {ModuleDiagnosticFormatter.Format(diagnostic)}");
            }

            hasErrors |= !inspection.IsValid;

            if (inspection.RequiredPrivilege == PrivilegeRequirement.Administrator && !isElevated)
            {
                var message =
                    $"The task '{task.Name}' requires elevation. Rerun the command from an elevated terminal.";
                hasErrors = true;
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
                records.Add(
                    new EngineTaskRecord(
                        task.Name,
                        resolvedModule.FullName,
                        task.RoleName,
                        ModuleResult.Failure(message)
                    )
                );
                return new EngineRunResult(mode, false, message, [message], records);
            }

            records.Add(
                new EngineTaskRecord(task.Name, resolvedModule.FullName, task.RoleName, result)
            );

            if (result.Failed)
            {
                return new EngineRunResult(
                    mode,
                    false,
                    result.Message ?? $"{task.Name} failed.",
                    [result.Message ?? $"{task.Name} failed."],
                    records
                );
            }
        }

        if (mode == ExecutionMode.Validate)
        {
            return hasErrors
                ? new EngineRunResult(mode, false, "Validation failed.", diagnostics, [])
                : new EngineRunResult(
                    mode,
                    true,
                    $"Validated {tasks.Count} task(s).",
                    diagnostics,
                    []
                );
        }

        var successMessage = mode switch
        {
            ExecutionMode.Check =>
                $"Check completed. {records.Count(static record => record.Result.Changed)} task(s) would change.",
            ExecutionMode.Apply =>
                $"Apply completed. {records.Count(static record => record.Result.Changed)} task(s) changed.",
            _ => $"Completed {records.Count} task(s).",
        };

        return new EngineRunResult(mode, true, successMessage, [], records);
    }
}
