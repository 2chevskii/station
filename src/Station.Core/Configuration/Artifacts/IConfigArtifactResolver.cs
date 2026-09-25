using Station.Core.Configuration.Discovery;

namespace Station.Core.Configuration.Artifacts;

internal interface IConfigArtifactResolver
{
    IReadOnlyList<ResolvedConfigArtifact> EnumerateRootVarArtifacts( DiscoveredConfigTree tree );

    ResolvedConfigArtifact? ResolveRoleDefaultsArtifact(
        DiscoveredDirectory roleDirectory,
        string roleFullName
    );

    ResolvedConfigArtifact ResolveRoleTaskArtifact(
        DiscoveredDirectory roleDirectory,
        string roleFullName
    );
}