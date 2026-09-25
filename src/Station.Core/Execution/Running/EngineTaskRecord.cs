namespace Station.Core.Execution.Running;

public sealed record EngineTaskRecord(
    string TaskName,
    string ModuleName,
    string? RoleName,
    ModuleResult Result
);
