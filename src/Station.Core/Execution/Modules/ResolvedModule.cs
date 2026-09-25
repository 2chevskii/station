namespace Station.Core.Execution.Modules;

internal sealed record ResolvedModule(
    string ProviderName,
    string ShortName,
    string FullName,
    ModuleSchema Schema,
    string? AssemblyPath = null
);
