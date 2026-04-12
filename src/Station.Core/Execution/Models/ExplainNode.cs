namespace Station.Core.Execution.Models;

public sealed record ExplainNode(
    string Text,
    ExplainNodeKind Kind,
    IReadOnlyList<ExplainNode> Children);
