namespace Station.Core.Execution.Modules;

internal sealed record ModuleSchema(
    Type ModuleType,
    string Name,
    string? Description,
    IReadOnlyList<ModuleParameterSchema> Parameters
);
