using Station.Abstractions.Enums;
using Station.Abstractions.Interfaces;
using Station.Abstractions.Models;

namespace Station.Tests.Plugins;

public sealed class AdminPluginModule : IConfigModule
{
    public ModuleDescriptor Descriptor { get; } = new("plugin.admin", "1.0.0", "Admin-only test module.");

    public ValueTask<ModuleInspectionResult> InspectAsync(ModuleExecutionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModuleInspectionResult.Success(PrivilegeRequirement.Administrator));

    public ValueTask<ModuleResult> ExecuteAsync(ModuleExecutionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModuleResult.ChangedResult("unexpected execution"));
}
