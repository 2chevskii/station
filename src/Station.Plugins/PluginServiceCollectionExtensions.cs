using Microsoft.Extensions.DependencyInjection;

using Station.Plugins.Caching;
using Station.Plugins.Manifests;
using Station.Plugins.Providers;
using Station.Plugins.Providers.Git;
using Station.Plugins.Providers.Local;
using Station.Plugins.Providers.Npm;
using Station.Plugins.Providers.Nuget;
using Station.Plugins.Providers.Url;
using Station.Plugins.Restoration;

namespace Station.Plugins;

public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddStationPlugins( this IServiceCollection services )
    {
        services.AddSingleton<PluginManifestReader>();
        services.AddSingleton<PluginCacheStore>();
        services.AddSingleton<PluginRestoreInfrastructure>();
        services.AddSingleton<IPluginProvider, LocalPluginProvider>();
        services.AddSingleton<IPluginProvider, GitPluginProvider>();
        services.AddSingleton<IPluginProvider, UrlPluginProvider>();
        services.AddSingleton<IPluginProvider, NuGetPluginProvider>();
        services.AddSingleton<IPluginProvider, NpmPluginProvider>();
        services.AddSingleton<PluginProviderRegistry>();
        services.AddSingleton( provider => new PluginSourceRestorer(
            provider.GetRequiredService<PluginProviderRegistry>()
        ) );

        return services;
    }
}
