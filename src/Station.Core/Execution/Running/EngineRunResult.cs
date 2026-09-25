using Station.Abstractions.Execution;

namespace Station.Core.Execution.Running;

public sealed record EngineRunResult(
    ExecutionMode Mode,
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<EngineTaskRecord> Tasks
)
{
    public int ChangedCount => Tasks.Count( static task => task.Result.Changed );
}