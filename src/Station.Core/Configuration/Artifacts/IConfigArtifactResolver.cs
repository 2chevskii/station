using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;

namespace Station.Core.Configuration.Artifacts;

internal interface IConfigArtifactResolver
{
    IReadOnlyList<ResolvedConfigArtifact> EnumerateRootVarArtifacts(DiscoveredConfigTree tree);

    ResolvedConfigArtifact? ResolveRoleDefaultsArtifact(
        DiscoveredDirectory roleDirectory,
        string roleFullName
    );

    ResolvedConfigArtifact ResolveRoleTaskArtifact(
        DiscoveredDirectory roleDirectory,
        string roleFullName
    );
}
