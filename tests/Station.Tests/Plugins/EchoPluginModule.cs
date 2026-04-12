using System.Text.Json.Nodes;

using Station.Abstractions.Interfaces;
using Station.Abstractions.Models;

namespace Station.Tests.Plugins;

public sealed class EchoPluginModule : IConfigModule
{
    public ModuleDescriptor Descriptor { get; } = new("plugin.echo", "1.0.0", "Echo test module.");

    public ValueTask<ModuleInspectionResult> InspectAsync(ModuleExecutionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModuleInspectionResult.Success());

    public ValueTask<ModuleResult> ExecuteAsync(ModuleExecutionRequest request, CancellationToken cancellationToken)
    {
        var changedNode = request.Parameters["changed"];
        var changed = changedNode switch
        {
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolValue) => boolValue,
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue) &&
                bool.TryParse(stringValue, out var parsed) => parsed,
            null => true,
            _ => false,
        };
        var message = request.Parameters["message"]?.GetValue<string>();
        return ValueTask.FromResult(changed
            ? ModuleResult.ChangedResult(message)
            : ModuleResult.NoChange(message));
    }
}
