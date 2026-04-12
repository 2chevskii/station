using Station.Abstractions.Attributes;
using Station.Abstractions.Models;

namespace Station.Tests.Plugins;

[Module("echo", "Echo test module.")]
public sealed class EchoPluginModule : ConfigModule
{
    public EchoPluginModule(ModuleContext context)
        : base(context)
    {
    }

    [Parameter]
    public bool Changed { get; set; } = true;

    [Parameter]
    public string? Message { get; set; }

    public override ValueTask<ModuleResult> ExecuteAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Changed
            ? ModuleResult.ChangedResult(Message)
            : ModuleResult.NoChange(Message));
}
