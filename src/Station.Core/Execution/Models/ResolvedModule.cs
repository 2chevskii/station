namespace Station.Core.Execution.Models;

internal sealed record ResolvedModule(
    string ProviderName,
    string ShortName,
    string FullName,
    ModuleSchema Schema);
