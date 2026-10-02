using Station.Core.Configuration.Plugins;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;

namespace Station.Core.Plugins.Restoration;

internal sealed class PluginRestorer
{
    private readonly PluginManifestLoader pluginManifestLoader;
    private readonly PluginSourceRestorer sourceRestorer;

    public PluginRestorer(
        PluginManifestLoader pluginManifestLoader,
        PluginSourceRestorer sourceRestorer
    )
    {
        this.pluginManifestLoader = pluginManifestLoader;
        this.sourceRestorer = sourceRestorer;
    }

    public async Task<PluginRestoreResult> RestoreAsync(
        string configRoot,
        IReadOnlyList<PluginRequirement> requirements,
        Action<string>? log = null,
        CancellationToken cancellationToken = default(CancellationToken),
        string? cacheRootOverride = null
    )
    {
        if ( requirements.Count == 0 )
            return new PluginRestoreResult( true, "No plugin requirements.", [], [], [] );

        var diagnostics = new List<string>();
        var records = new List<PluginRestoreRecord>();
        var manifests = new List<ResolvedPluginManifest>();

        foreach ( var requirement in requirements )
            try
            {
                var restored = await sourceRestorer.RestoreAsync(
                    configRoot,
                    requirement,
                    log,
                    cancellationToken,
                    cacheRootOverride
                );
                var manifest = pluginManifestLoader.Load( restored.PluginRoot );
                manifests.Add( manifest );
                records.Add(
                    new PluginRestoreRecord(
                        requirement.DisplaySource,
                        manifest.Name,
                        manifest.Version,
                        manifest.PluginRoot,
                        restored.CacheHit,
                        restored.IsLocal
                    )
                );
            }
            catch ( Exception exception )
                when ( exception is ConfigurationException or IOException or HttpRequestException )
            {
                diagnostics.Add( $"{requirement.DisplaySource}: {exception.Message}" );
            }

        if ( diagnostics.Count > 0 )
            return new PluginRestoreResult(
                false,
                "Plugin restore failed.",
                diagnostics,
                records,
                manifests
            );

        return new PluginRestoreResult(
            true,
            $"Restored {manifests.Count} plugin(s).",
            [],
            records,
            manifests
        );
    }
}
