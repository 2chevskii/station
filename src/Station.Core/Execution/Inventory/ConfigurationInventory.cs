using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;
using Station.Core.Execution.Modules;
using Station.Core.Execution.Roles;
using Station.Core.Plugins.Restoration;

namespace Station.Core.Execution.Inventory;

internal sealed class ConfigurationInventory(
    ConfigLoader configLoader,
    PluginRestorer pluginRestorer,
    IEnumerable<ModuleRegistration> builtInModules,
    PluginCacheDirectoryResolver cacheDirectoryResolver
)
{
    public Task<InventoryResult> ListRolesAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables,
        string? pluginCacheDirectory,
        Action<string>? log,
        CancellationToken cancellationToken
    ) =>
        ListAsync(
            configRoot,
            cliVariables,
            pluginCacheDirectory,
            "roles",
            BuildRoles,
            log,
            cancellationToken
        );

    public Task<InventoryResult> ListModulesAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables,
        string? pluginCacheDirectory,
        Action<string>? log,
        CancellationToken cancellationToken
    ) =>
        ListAsync(
            configRoot,
            cliVariables,
            pluginCacheDirectory,
            "modules",
            BuildModules,
            log,
            cancellationToken
        );

    private async Task<InventoryResult> ListAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables,
        string? pluginCacheDirectory,
        string kind,
        Func<
            string,
            IReadOnlyList<ResolvedPluginManifest>,
            IReadOnlyList<AvailableComponent>
        > build,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var (root, requirements, variables) = configLoader.LoadPluginRequirements(
                configRoot,
                cliVariables
            );
            var cacheRoot = cacheDirectoryResolver.Resolve(
                root,
                variables,
                pluginCacheDirectory
            );
            var restore = await pluginRestorer.RestoreAsync(
                root,
                requirements,
                log,
                cancellationToken,
                cacheRoot
            );
            if ( !restore.Succeeded )
                return new InventoryResult( false, restore.Message, restore.Diagnostics, [] );

            var components = build( root, restore.RestoredPlugins );
            cancellationToken.ThrowIfCancellationRequested();
            return new InventoryResult(
                true,
                $"Found {components.Count} available {kind}.",
                [],
                components
            );
        }
        catch ( Exception exception ) when ( exception is ConfigurationException or IOException )
        {
            return new InventoryResult( false, exception.Message, [exception.Message], [] );
        }
    }

    private IReadOnlyList<AvailableComponent> BuildRoles(
        string configRoot,
        IReadOnlyList<ResolvedPluginManifest> plugins
    )
    {
        var catalog = RoleCatalog.Create( configLoader.DiscoverLocalRoles( configRoot ), plugins );
        return catalog
            .All.Select( role => new AvailableComponent(
                role.FullName,
                configLoader.ReadRoleDescription( role ),
                FormatSource( role.ProviderName, role.RoleRoot, configRoot )
            ) )
            .ToArray();
    }

    private IReadOnlyList<AvailableComponent> BuildModules(
        string configRoot,
        IReadOnlyList<ResolvedPluginManifest> plugins
    )
    {
        var catalog = ModuleCatalog.Create(
            builtInModules,
            configLoader.DiscoverLocalModulePaths( configRoot ),
            plugins
        );
        return catalog
            .All.Select( module => new AvailableComponent(
                module.FullName,
                module.Schema.Description,
                module.AssemblyPath is null
                    ? module.ProviderName
                    : FormatSource( module.ProviderName, module.AssemblyPath, configRoot )
            ) )
            .ToArray();
    }

    private static string FormatSource( string providerName, string path, string configRoot )
    {
        var relativePath = Path.GetRelativePath( configRoot, path ).Replace( '\\', '/' );
        return $"{providerName}: {relativePath}";
    }
}
