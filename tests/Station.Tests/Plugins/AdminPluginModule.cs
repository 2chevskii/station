using Station.Abstractions.Enums;
using Station.Abstractions.Attributes;
using Station.Abstractions.Models;

namespace Station.Tests.Plugins;

[Module("admin", "Admin-only test module.")]
public sealed class AdminPluginModule : ConfigModule
{
    public AdminPluginModule(ModuleContext context)
        : base(context)
    {
    }

    public override ValueTask<ModuleInspectionResult> InspectAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModuleInspectionResult.Success(PrivilegeRequirement.Administrator));

    public override ValueTask<ModuleResult> ExecuteAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModuleResult.ChangedResult("unexpected execution"));
}
