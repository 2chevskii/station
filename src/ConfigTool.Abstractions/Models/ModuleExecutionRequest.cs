using System.Text.Json.Nodes;

namespace ConfigTool.Abstractions;

public sealed record ModuleExecutionRequest(
    ExecutionMode Mode,
    string TaskName,
    string ConfigRoot,
    string TaskRoot,
    string WorkingDirectory,
    string? RoleName,
    string? RoleRoot,
    JsonObject Parameters,
    IReadOnlyDictionary<string, string> Variables,
    bool IsElevated);
