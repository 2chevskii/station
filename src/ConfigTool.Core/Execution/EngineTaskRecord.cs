using ConfigTool.Abstractions;

namespace ConfigTool.Core;

public sealed record EngineTaskRecord(
    string TaskName,
    string ModuleName,
    string? RoleName,
    ModuleResult Result);
