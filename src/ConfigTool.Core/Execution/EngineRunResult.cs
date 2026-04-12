using ConfigTool.Abstractions;

namespace ConfigTool.Core;

public sealed record EngineRunResult(
    ExecutionMode Mode,
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<EngineTaskRecord> Tasks)
{
    public int ChangedCount => Tasks.Count(static task => task.Result.Changed);
}
