using Station.Abstractions.Models;
using Station.Plugins.Providers;

namespace Station.Plugins.Services;

internal sealed class PluginProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPluginProvider> providersByName;

    public PluginProviderRegistry(IEnumerable<IPluginProvider>? providers = null)
    {
        providersByName = (providers ?? CreateDefaults()).ToDictionary(
            provider => provider.Name,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public IPluginProvider Resolve(string providerName)
    {
        if (providersByName.TryGetValue(providerName, out var provider))
        {
            return provider;
        }

        throw new ConfigurationException($"Unsupported plugin provider '{providerName}'.");
    }

    private static IReadOnlyList<IPluginProvider> CreateDefaults() =>
        [
            new LocalPluginProvider(),
            new GitPluginProvider(),
            new UrlPluginProvider(),
            new NuGetPluginProvider(),
            new NpmPluginProvider(),
        ];
}
