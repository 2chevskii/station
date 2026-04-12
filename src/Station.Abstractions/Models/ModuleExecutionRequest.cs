using System.Text.Json.Nodes;

using Station.Abstractions.Enums;

namespace Station.Abstractions.Models;

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
