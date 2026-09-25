using Station.Abstractions.Models;

namespace Station.Core.Configuration.Models;

public sealed record LoadedTask(
    string Name,
    string ModuleName,
    ConfigObject Parameters,
    string TaskRoot,
    string? RoleName,
    string? RoleRoot,
    VariableSet Variables
);
