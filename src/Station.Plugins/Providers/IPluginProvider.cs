using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Sources;
using Station.Plugins.Restoration;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;

namespace Station.Plugins.Providers;

internal interface IPluginProvider
{
    string Name { get; }

    Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    );

    RestoredPluginLocation Restore(
        string configRoot,
        PluginDeclaration declaration,
        PluginSourceSnapshot source,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log
    );
}
