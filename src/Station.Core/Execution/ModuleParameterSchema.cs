using System.Reflection;

namespace Station.Core.Execution;

internal sealed record ModuleParameterSchema(
    string Name,
    IReadOnlyList<string> Aliases,
    bool Required,
    string? Description,
    PropertyInfo Property);
