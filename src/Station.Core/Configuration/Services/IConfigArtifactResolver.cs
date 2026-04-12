using Station.Core.Configuration.Models;

namespace Station.Core.Configuration.Services;

internal interface IConfigArtifactResolver
{
    IReadOnlyList<ResolvedConfigArtifact> EnumerateRootVarArtifacts(DiscoveredConfigTree tree);

    ResolvedConfigArtifact? ResolveRoleDefaultsArtifact(DiscoveredDirectory roleDirectory, string roleFullName);

    ResolvedConfigArtifact ResolveRoleTaskArtifact(DiscoveredDirectory roleDirectory, string roleFullName);
}
