using Station.Abstractions.Configuration;

namespace Station.Plugins.Providers;

internal sealed class PluginProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPluginProvider> providersByName;

    public PluginProviderRegistry( IEnumerable<IPluginProvider> providers )
    {
        providersByName = providers.ToDictionary(
            provider => provider.Name,
            StringComparer.OrdinalIgnoreCase
        );
    }
    public IPluginProvider Resolve( string providerName )
    {
        if ( providersByName.TryGetValue( providerName, out var provider ) )
            return provider;

        throw new ConfigurationException( $"Unsupported plugin provider '{providerName}'." );
    }

}
