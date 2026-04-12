namespace Station.Core.Execution;

public sealed record ExplainResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    string Diagram);
