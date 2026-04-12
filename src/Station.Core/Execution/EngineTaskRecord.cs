using Station.Abstractions;

namespace Station.Core;

public sealed record EngineTaskRecord(
    string TaskName,
    string ModuleName,
    string? RoleName,
    ModuleResult Result);
