using Microsoft.Extensions.DependencyInjection;

using Station.Core.Configuration.Artifacts;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Core.Execution.Inventory;
using Station.Core.Execution.Modules;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Core.Plugins.Restoration;
using Station.Modules.Builtin.Apt;
using Station.Modules.Builtin.Dotfile;
using Station.Modules.Builtin.Scoop;
using Station.Modules.Builtin.Script;
using Station.Modules.Builtin.Winget;

namespace Station.Core;

public static class StationServiceCollectionExtensions
{
    public static IServiceCollection AddStationCore( this IServiceCollection services )
    {
        services.AddSingleton<ConfigTreeDiscovery>();
        services.AddSingleton<LocalConfigurationDiscovery>();
        services.AddSingleton<IConfigArtifactResolver, ConfigArtifactResolver>();
        services.AddSingleton<VariableScopeLoader>();
        services.AddSingleton<TaskLoader>();
        services.AddSingleton<PluginRequirementParser>();
        services.AddSingleton<PluginModuleExportParser>();
        services.AddSingleton<PluginManifestLoader>();
        services.AddSingleton<PluginRestorer>();
        services.AddSingleton<PluginCacheDirectoryResolver>();
        services.AddSingleton<ModuleActivator>();
        services.AddSingleton<ModuleBinder>();
        services.AddSingleton<ConfigurationInventory>();
        services.AddSingleton<TaskExecutionPipeline>();
        services.AddSingleton<ExplainTreeBuilder>();

        services.AddSingleton( provider => new ConfigLoader(
            provider.GetRequiredService<ConfigTreeDiscovery>(),
            provider.GetRequiredService<LocalConfigurationDiscovery>(),
            provider.GetRequiredService<PluginRequirementParser>(),
            provider.GetRequiredService<PluginManifestLoader>(),
            provider.GetRequiredService<TaskLoader>(),
            provider.GetRequiredService<VariableScopeLoader>(),
            provider.GetRequiredService<YamlConfigurationReader>()
        ) );
        services.AddSingleton( provider => new ConfigEngine(
            provider.GetRequiredService<ConfigLoader>(),
            provider.GetRequiredService<PluginRestorer>(),
            provider.GetServices<ModuleRegistration>(),
            provider.GetRequiredService<ConfigurationInventory>(),
            provider.GetRequiredService<TaskExecutionPipeline>(),
            provider.GetRequiredService<ExplainTreeBuilder>(),
            provider.GetRequiredService<PluginCacheDirectoryResolver>()
        ) );

        services.AddSingleton( new ModuleRegistration( "station.builtin", typeof( AptModule ) ) );
        services.AddSingleton( new ModuleRegistration( "station.builtin", typeof( CopyModule ) ) );
        services.AddSingleton( new ModuleRegistration( "station.builtin", typeof( TemplateModule ) ) );
        services.AddSingleton( new ModuleRegistration( "station.builtin", typeof( ScriptModule ) ) );
        services.AddSingleton( new ModuleRegistration( "station.builtin", typeof( ScoopModule ) ) );
        services.AddSingleton( new ModuleRegistration( "station.builtin", typeof( WingetModule ) ) );

        return services;
    }
}
