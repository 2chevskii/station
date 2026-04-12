using Station.Abstractions.Models;
using Station.Core.Configuration.Models;

namespace Station.Core.Configuration.Services;

internal sealed record ConfigEvaluationContext(
    string ConfigRoot,
    ResolvedConfigArtifact Artifact,
    VariableSet VisibleVariables,
    string? RoleName,
    string? RoleRoot);
