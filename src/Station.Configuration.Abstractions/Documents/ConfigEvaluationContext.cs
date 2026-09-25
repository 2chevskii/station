using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;

namespace Station.Configuration.Abstractions.Documents;

public sealed record ConfigEvaluationContext(
    string ConfigRoot,
    ResolvedConfigArtifact Artifact,
    VariableSet VisibleVariables,
    string? RoleName,
    string? RoleRoot
);
