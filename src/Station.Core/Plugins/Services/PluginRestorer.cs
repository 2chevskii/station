using Station.Abstractions.Models;
using Station.Core.Configuration.Models;
using Station.Core.Configuration.Services;
using Station.Core.Plugins.Models;
using Station.Plugins.Abstractions;
using Station.Plugins.Services;
using Station.Utilities.Processes;

namespace Station.Core.Plugins.Services;

internal sealed class PluginRestorer
{
    private readonly PluginManifestLoader pluginManifestLoader;
    private readonly PluginSourceRestorer sourceRestorer;

    public PluginRestorer(
        PluginManifestLoader pluginManifestLoader,
        IProcessRunner processRunner
    )
    {
        this.pluginManifestLoader = pluginManifestLoader;
        sourceRestorer = new PluginSourceRestorer(processRunner);
    }

    public async Task<PluginRestoreResult> RestoreAsync(
        string configRoot,
        IReadOnlyList<PluginDeclaration> declarations,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        if (declarations.Count == 0)
        {
            return new PluginRestoreResult(true, "No plugins declared.", [], [], []);
        }

        var diagnostics = new List<string>();
        var records = new List<PluginRestoreRecord>();
        var manifests = new List<ResolvedPluginManifest>();

        foreach (var declaration in declarations)
        {
            try
            {
                var restored = await sourceRestorer.RestoreAsync(
                    configRoot,
                    declaration,
                    log,
                    cancellationToken
                );
                var manifest = pluginManifestLoader.Load(restored.PluginRoot);
                manifests.Add(manifest);
                records.Add(
                    new PluginRestoreRecord(
                        declaration.DisplaySource,
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
                diagnostics.Add($"{declaration.DisplaySource}: {exception.Message}");
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
