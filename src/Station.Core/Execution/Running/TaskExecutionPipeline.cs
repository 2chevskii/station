using Station.Abstractions.Execution;
using Station.Core.Configuration.Loading;
using Station.Core.Execution.Modules;
using Station.Utilities.Privileges;

namespace Station.Core.Execution.Running;

internal sealed class TaskExecutionPipeline(
    ModuleBinder moduleBinder,
    IPrivilegeDetector privilegeDetector,
    ITemplateEngine templateEngine,
    TimeProvider timeProvider
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
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<string>();
        var hasErrors = false;
        var isElevated = privilegeDetector.IsElevated();
        var records = new List<EngineTaskRecord>();

        foreach ( var task in tasks )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolvedModule resolvedModule;
            try
            {
                resolvedModule = moduleCatalog.Resolve( task.ModuleName );
            }
            catch ( ConfigurationException exception )
            {
                hasErrors = true;
                diagnostics.Add( $"{task.Name}: error: {exception.Message}" );
                if ( mode != ExecutionMode.Validate )
                    return new EngineRunResult( mode, false, "Validation failed.", diagnostics, records );

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
                timeProvider
            );
            var binding = moduleBinder.Bind(
                resolvedModule.Schema,
                context,
                task.Parameters,
                mode == ExecutionMode.Validate
            );
            cancellationToken.ThrowIfCancellationRequested();

            foreach ( var diagnostic in binding.Diagnostics )
                diagnostics.Add( $"{task.Name}: {ModuleDiagnosticFormatter.Format( diagnostic )}" );

            if ( binding.HasErrors )
            {
                hasErrors = true;
                if ( mode == ExecutionMode.Validate )
                    continue;

                return new EngineRunResult( mode, false, "Validation failed.", diagnostics, records );
            }

            if ( binding.IsDeferred )
                continue;

            log?.Invoke( $"Inspecting {task.Name} [{resolvedModule.FullName}]" );

            ModuleInspectionResult inspection;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                inspection = await binding.Module!.InspectAsync( cancellationToken );
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
            {
                throw;
            }
            catch ( Exception exception )
            {
                hasErrors = true;
                diagnostics.Add( $"{task.Name}: inspection failed: {exception.Message}" );
                if ( mode == ExecutionMode.Validate )
                    continue;

                return new EngineRunResult( mode, false, "Validation failed.", diagnostics, records );
            }

            foreach ( var diagnostic in inspection.Diagnostics )
                diagnostics.Add( $"{task.Name}: {ModuleDiagnosticFormatter.Format( diagnostic )}" );

            hasErrors |= !inspection.IsValid;
            if ( !inspection.IsValid && mode != ExecutionMode.Validate )
                return new EngineRunResult( mode, false, "Validation failed.", diagnostics, records );

            if ( inspection.RequiredPrivilege == PrivilegeRequirement.Administrator && !isElevated )
            {
                var message =
                    $"The task '{task.Name}' requires elevation. Rerun the command from an elevated terminal.";
                hasErrors = true;
                diagnostics.Add( $"{task.Name}: error: {message}" );
                if ( mode == ExecutionMode.Validate )
                    continue;

                return new EngineRunResult( mode, false, message, diagnostics, records );
            }

            if ( mode == ExecutionMode.Validate )
                continue;

            log?.Invoke( $"Running {task.Name} [{resolvedModule.FullName}]" );

            ModuleResult result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await binding.Module!.ExecuteAsync( cancellationToken );
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
            {
                throw;
            }
            catch ( Exception exception )
            {
                var message = $"{task.Name}: execution failed: {exception.Message}";
                diagnostics.Add( message );
                records.Add(
                    new EngineTaskRecord(
                        task.Name,
                        resolvedModule.FullName,
                        task.RoleName,
                        ModuleResult.Failure( message )
                    )
                );
                return new EngineRunResult( mode, false, message, diagnostics, records );
            }

            records.Add(
                new EngineTaskRecord( task.Name, resolvedModule.FullName, task.RoleName, result )
            );

            if ( result.Failed )
            {
                var message = result.Message ?? $"{task.Name} failed.";
                diagnostics.Add( $"{task.Name}: error: {message}" );
                return new EngineRunResult(
                    mode,
                    false,
                    message,
                    diagnostics,
                    records
                );
            }
        }

        if ( mode == ExecutionMode.Validate )
            return hasErrors
                ? new EngineRunResult( mode, false, "Validation failed.", diagnostics, [] )
                : new EngineRunResult(
                    mode,
                    true,
                    $"Validated {tasks.Count} task(s).",
                    diagnostics,
                    []
                );

        var successMessage = mode switch
        {
            ExecutionMode.Check =>
                $"Check completed. {records.Count( static record => record.Result.Changed )} task(s) would change.",
            ExecutionMode.Apply =>
                $"Apply completed. {records.Count( static record => record.Result.Changed )} task(s) changed.",
            var _ => $"Completed {records.Count} task(s).",
        };

        return new EngineRunResult( mode, true, successMessage, diagnostics, records );
    }
}
