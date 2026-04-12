using Station.Abstractions.Models;
using Station.Core.Configuration;
using Station.Core.Configuration.Services;
using Station.Core.Infrastructure.Processes;
using Station.Core.Plugins.Providers;
using Station.Core.Plugins.Services;

namespace Station.Core.Plugins;

internal sealed class PluginRestorer
{
    private readonly PluginManifestLoader pluginManifestLoader;
    private readonly PluginProviderRegistry pluginProviderRegistry;
    private readonly PluginRestoreInfrastructure restoreInfrastructure;

    public PluginRestorer(
        PluginManifestLoader pluginManifestLoader,
        LocalConfigurationDiscovery localConfigurationDiscovery,
        IProcessRunner processRunner,
        IEnumerable<IPluginProvider>? providers = null)
    {
        this.pluginManifestLoader = pluginManifestLoader;
        restoreInfrastructure = new PluginRestoreInfrastructure(
            localConfigurationDiscovery,
            processRunner
        );
        pluginProviderRegistry = new PluginProviderRegistry(providers);
    }

    public async Task<PluginRestoreResult> RestoreAsync(
        string configRoot,
        IReadOnlyList<PluginDeclaration> declarations,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
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
                var provider = pluginProviderRegistry.Resolve(declaration.Provider);
                var restored = await provider.RestoreAsync(
                    configRoot,
                    declaration,
                    restoreInfrastructure,
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
