using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Providers;
using Station.Utilities.Processes;

namespace Station.Plugins.Restoration;

public sealed class PluginSourceRestorer
{
    private readonly PluginProviderRegistry providerRegistry = new();
    private readonly IProcessRunner processRunner;

    public PluginSourceRestorer(IProcessRunner processRunner)
    {
        this.processRunner = processRunner;
    }

    public async Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginRequirement requirement,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        string? cacheRootOverride = null
    )
    {
        var infrastructure = new PluginRestoreInfrastructure(processRunner, cacheRootOverride);
        var provider = providerRegistry.Resolve(requirement.Provider);
        using var source = await provider.GetManifestAsync(
            configRoot,
            requirement,
            infrastructure,
            cancellationToken
        );
        return provider.Restore(configRoot, requirement, source, infrastructure, log);
    }
}
