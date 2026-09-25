namespace Station.Core.Execution.Planning;

public sealed record ExplainResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    ExplainNode? Plan
);
