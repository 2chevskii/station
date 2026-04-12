namespace Station.Abstractions;

public interface IConfigModule
{
    ModuleDescriptor Descriptor { get; }

    ValueTask<ModuleInspectionResult> InspectAsync(ModuleExecutionRequest request, CancellationToken cancellationToken);

    ValueTask<ModuleResult> ExecuteAsync(ModuleExecutionRequest request, CancellationToken cancellationToken);
}
