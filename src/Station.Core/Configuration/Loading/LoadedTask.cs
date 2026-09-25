
namespace Station.Core.Configuration.Loading;

public sealed record LoadedTask(
    string Name,
    string ModuleName,
    ConfigObject Parameters,
    string TaskRoot,
    string? RoleName,
    string? RoleRoot,
    VariableSet Variables
);
