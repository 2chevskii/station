using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;
using Station.Core.Configuration.Artifacts;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;
using Station.Plugins.Providers;
using Station.Utilities.Processes;

namespace Station.Core.Plugins.Restoration;

internal sealed class PluginRestorer
{
    private readonly PluginManifestLoader pluginManifestLoader;
    private readonly PluginSourceRestorer sourceRestorer;

    public PluginRestorer(PluginManifestLoader pluginManifestLoader, IProcessRunner processRunner)
    {
        this.pluginManifestLoader = pluginManifestLoader;
        sourceRestorer = new PluginSourceRestorer(processRunner);
    }

    public async Task<PluginRestoreResult> RestoreAsync(
        string configRoot,
        IReadOnlyList<PluginRequirement> requirements,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        string? cacheRootOverride = null
    )
    {
        if (requirements.Count == 0)
        {
            return new PluginRestoreResult(true, "No plugin requirements.", [], [], []);
        }

        var diagnostics = new List<string>();
        var records = new List<PluginRestoreRecord>();
        var manifests = new List<ResolvedPluginManifest>();

        foreach (var requirement in requirements)
        {
            try
            {
                var restored = await sourceRestorer.RestoreAsync(
                    configRoot,
                    requirement,
                    log,
                    cancellationToken,
                    cacheRootOverride
                );
                var manifest = pluginManifestLoader.Load(restored.PluginRoot);
                manifests.Add(manifest);
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
            catch (Exception exception)
                when (exception is ConfigurationException or IOException or HttpRequestException)
            {
                diagnostics.Add($"{requirement.DisplaySource}: {exception.Message}");
            }
        }

        if (diagnostics.Count > 0)
        {
            return new PluginRestoreResult(
                false,
                "Plugin restore failed.",
                diagnostics,
                records,
                manifests
            );
        }

        return new PluginRestoreResult(
            true,
            $"Restored {manifests.Count} plugin(s).",
            [],
            records,
            manifests
        );
    }
}
