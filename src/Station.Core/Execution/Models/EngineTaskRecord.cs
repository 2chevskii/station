using Station.Abstractions.Models;

namespace Station.Core.Execution;

public sealed record EngineTaskRecord(
    string TaskName,
    string ModuleName,
    string? RoleName,
    ModuleResult Result);
