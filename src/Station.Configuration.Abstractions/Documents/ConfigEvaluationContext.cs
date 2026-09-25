using Station.Abstractions.Configuration;

namespace Station.Configuration.Abstractions.Documents;

public sealed record ConfigEvaluationContext(
    string ConfigRoot,
    ResolvedConfigArtifact Artifact,
    VariableSet VisibleVariables,
    string? RoleName,
    string? RoleRoot
);
