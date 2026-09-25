using Station.Abstractions.Models;
using Station.Configuration.Abstractions.Models;

namespace Station.Configuration.Abstractions.Services;

public sealed record ConfigEvaluationContext(
    string ConfigRoot,
    ResolvedConfigArtifact Artifact,
    VariableSet VisibleVariables,
    string? RoleName,
    string? RoleRoot
);
