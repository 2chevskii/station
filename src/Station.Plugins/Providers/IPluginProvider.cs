using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;

namespace Station.Plugins.Providers;

internal interface IPluginProvider
{
    string Name { get; }

    Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    );

    RestoredPluginLocation Restore(
        string configRoot,
        PluginRequirement requirement,
        PluginSourceSnapshot source,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log
    );
}
