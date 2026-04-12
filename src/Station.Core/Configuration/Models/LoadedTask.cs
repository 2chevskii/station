using System.Text.Json.Nodes;

namespace Station.Core.Configuration.Models;

public sealed record LoadedTask(
    string Name,
    string ModuleName,
    JsonObject Parameters,
    string TaskRoot,
    string? RoleName,
    string? RoleRoot,
    VariableSet Variables);
