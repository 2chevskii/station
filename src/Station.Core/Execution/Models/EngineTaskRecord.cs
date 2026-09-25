namespace Station.Core.Execution.Models;

public sealed record EngineTaskRecord(
    string TaskName,
    string ModuleName,
    string? RoleName,
    ModuleResult Result
);
