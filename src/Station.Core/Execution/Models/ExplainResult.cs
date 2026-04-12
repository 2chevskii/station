namespace Station.Core.Execution.Models;

public sealed record ExplainResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    ExplainNode? Plan);
