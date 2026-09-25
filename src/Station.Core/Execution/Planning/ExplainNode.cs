namespace Station.Core.Execution.Planning;

public sealed record ExplainNode(
    string Text,
    ExplainNodeKind Kind,
    IReadOnlyList<ExplainNode> Children
);