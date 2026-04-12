namespace Station.Abstractions.Models;

public abstract class ConfigModule
{
    protected ConfigModule(ModuleContext context)
    {
        Context = context;
    }

    protected ModuleContext Context { get; }

    public virtual ValueTask<ModuleInspectionResult> InspectAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModuleInspectionResult.Success());

    public abstract ValueTask<ModuleResult> ExecuteAsync(CancellationToken cancellationToken);
}
