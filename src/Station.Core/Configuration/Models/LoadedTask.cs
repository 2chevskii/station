using System.Text.Json.Nodes;
using Station.Abstractions.Models;

namespace Station.Core.Configuration;

public sealed record LoadedTask(
    string Name,
    string ModuleName,
    JsonObject Parameters,
    string TaskRoot,
    string? RoleName,
    string? RoleRoot,
    VariableSet Variables);
