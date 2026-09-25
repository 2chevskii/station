using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Sources;
using Station.Plugins.Providers;
using Station.Utilities.Processes;

namespace Station.Plugins.Restoration;

public sealed class PluginSourceRestorer
{
    private readonly PluginProviderRegistry providerRegistry = new();
    private readonly PluginRestoreInfrastructure infrastructure;

    public PluginSourceRestorer(IProcessRunner processRunner)
    {
        infrastructure = new PluginRestoreInfrastructure(processRunner);
    }

    public async Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginDeclaration declaration,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        var provider = providerRegistry.Resolve(declaration.Provider);
        using var source = await provider.GetManifestAsync(
            configRoot,
            declaration,
            infrastructure,
            cancellationToken
        );
        return provider.Restore(configRoot, declaration, source, infrastructure, log);
    }
}
