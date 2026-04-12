using System.Text.Json.Nodes;
namespace ConfigTool.Core;

public sealed record LoadedTask(
    string Name,
    string ModuleName,
    JsonObject Parameters,
    string TaskRoot,
    string? RoleName,
    string? RoleRoot,
    IReadOnlyDictionary<string, string> Variables);
