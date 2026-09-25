using System.Reflection;

namespace Station.Core.Execution.Modules;

internal sealed record ModuleSchema(
    Type ModuleType,
    string Name,
    string? Description,
    ConstructorInfo Constructor,
    IReadOnlyList<ModuleParameterSchema> Parameters
);