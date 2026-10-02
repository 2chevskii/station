using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Providers;

namespace Station.Plugins.Restoration;

public sealed class PluginSourceRestorer
{
    private readonly PluginProviderRegistry providerRegistry;

    internal PluginSourceRestorer( PluginProviderRegistry providerRegistry )
    {
        this.providerRegistry = providerRegistry;
    }

    public async Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginRequirement requirement,
        Action<string>? log = null,
        CancellationToken cancellationToken = default(CancellationToken),
        string? cacheRootOverride = null
    )
    {
        var provider = providerRegistry.Resolve( requirement.Provider );
        using var source = await provider.GetManifestAsync(
            configRoot,
            requirement,
            cancellationToken
        );
        return provider.Restore( configRoot, requirement, source, log, cacheRootOverride );
    }
}
