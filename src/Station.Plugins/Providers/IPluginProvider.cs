using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;

namespace Station.Plugins.Providers;

internal interface IPluginProvider
{
    string Name { get; }

    Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        CancellationToken cancellationToken
    );

    RestoredPluginLocation Restore(
        string configRoot,
        PluginRequirement requirement,
        PluginSourceSnapshot source,
        Action<string>? log,
        string? cacheRootOverride
    );
}
