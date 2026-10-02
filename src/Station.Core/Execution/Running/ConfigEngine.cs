using Station.Abstractions.Execution;
using Station.Core.Configuration.Loading;
using Station.Core.Execution.Inventory;
using Station.Core.Execution.Modules;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Roles;
using Station.Core.Plugins.Restoration;

namespace Station.Core.Execution.Running;

public sealed class ConfigEngine
{
    private readonly IReadOnlyList<ModuleRegistration> builtInModules;
    private readonly ConfigLoader configLoader;
    private readonly PluginRestorer pluginRestorer;
    private readonly ConfigurationInventory inventory;
    private readonly TaskExecutionPipeline executionPipeline;
    private readonly ExplainTreeBuilder explainTreeBuilder;
    private readonly PluginCacheDirectoryResolver cacheDirectoryResolver;

    internal ConfigEngine(
        ConfigLoader configLoader,
        PluginRestorer pluginRestorer,
        IEnumerable<ModuleRegistration> builtInModules,
        ConfigurationInventory inventory,
        TaskExecutionPipeline executionPipeline,
        ExplainTreeBuilder explainTreeBuilder,
        PluginCacheDirectoryResolver cacheDirectoryResolver
    )
    {
        this.configLoader = configLoader;
        this.pluginRestorer = pluginRestorer;
        this.builtInModules = builtInModules.ToArray();
        this.inventory = inventory;
        this.executionPipeline = executionPipeline;
        this.explainTreeBuilder = explainTreeBuilder;
        this.cacheDirectoryResolver = cacheDirectoryResolver;
    }

    public Task<InventoryResult> ListRolesAsync(
        string configRoot,
        Action<string>? log = null,
        CancellationToken cancellationToken = default(CancellationToken),
        IReadOnlyDictionary<string, string>? cliVariables = null,
        string? pluginCacheDirectory = null
    ) =>
        inventory.ListRolesAsync(
            configRoot,
            cliVariables,
            pluginCacheDirectory,
            log,
            cancellationToken
        );

    public Task<InventoryResult> ListModulesAsync(
        string configRoot,
        Action<string>? log = null,
        CancellationToken cancellationToken = default(CancellationToken),
        IReadOnlyDictionary<string, string>? cliVariables = null,
        string? pluginCacheDirectory = null
    ) =>
        inventory.ListModulesAsync(
            configRoot,
            cliVariables,
            pluginCacheDirectory,
            log,
            cancellationToken
        );

    public async Task<PluginRestoreResult> RestoreAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default(CancellationToken),
        string? pluginCacheDirectory = null
    )
    {
        try
        {
            var configuration = configLoader.Load( configRoot, cliVariables );
            var cacheRoot = cacheDirectoryResolver.Resolve(
                configuration.ConfigRoot,
                configuration.RootVariables,
                pluginCacheDirectory
            );
            return await pluginRestorer.RestoreAsync(
                configuration.ConfigRoot,
                configuration.PluginRequirements,
                log,
                cancellationToken,
                cacheRoot
            );
        }
        catch ( Exception exception ) when ( exception is ConfigurationException or IOException )
        {
            return new PluginRestoreResult( false, exception.Message, [exception.Message], [], [] );
        }
    }

    public async Task<EngineRunResult> RunAsync(
        string configRoot,
        ExecutionMode mode,
        IReadOnlyDictionary<string, string>? cliVariables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default(CancellationToken),
        string? pluginCacheDirectory = null
    )
    {
        if ( mode == ExecutionMode.Restore )
            throw new InvalidOperationException( "Use RestoreAsync for restore operations." );

        PreparedExecution prepared;

        try
        {
            prepared = await PrepareAsync(
                configRoot,
                cliVariables,
                pluginCacheDirectory,
                log,
                cancellationToken
            );

            if ( !prepared.RestoreResult.Succeeded )
                return new EngineRunResult(
                    mode,
                    false,
                    prepared.RestoreResult.Message,
                    prepared.RestoreResult.Diagnostics,
                    []
                );
        }
        catch ( Exception exception ) when ( exception is ConfigurationException or IOException )
        {
            return new EngineRunResult( mode, false, exception.Message, [exception.Message], [] );
        }

        return await executionPipeline.RunAsync(
            prepared.Configuration,
            prepared.ModuleCatalog!,
            prepared.Tasks!,
            mode,
            log,
            cancellationToken
        );
    }

    public async Task<ExplainResult> ExplainAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default(CancellationToken),
        string? pluginCacheDirectory = null
    )
    {
        PreparedExecution prepared;

        try
        {
            prepared = await PrepareAsync(
                configRoot,
                cliVariables,
                pluginCacheDirectory,
                log,
                cancellationToken
            );

            if ( !prepared.RestoreResult.Succeeded )
                return new ExplainResult(
                    false,
                    prepared.RestoreResult.Message,
                    prepared.RestoreResult.Diagnostics,
                    null
                );
        }
        catch ( Exception exception ) when ( exception is ConfigurationException or IOException )
        {
            return new ExplainResult( false, exception.Message, [exception.Message], null );
        }

        return explainTreeBuilder.Build(
            prepared.Configuration,
            prepared.ModuleCatalog!,
            prepared.Tasks!
        );
    }

    private async Task<PreparedExecution> PrepareAsync(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables,
        string? pluginCacheDirectory,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        var configuration = configLoader.Load( configRoot, cliVariables );
        var cacheRoot = cacheDirectoryResolver.Resolve(
            configuration.ConfigRoot,
            configuration.RootVariables,
            pluginCacheDirectory
        );
        var restoreResult = await pluginRestorer.RestoreAsync(
            configuration.ConfigRoot,
            configuration.PluginRequirements,
            log,
            cancellationToken,
            cacheRoot
        );

        if ( !restoreResult.Succeeded )
            return new PreparedExecution( configuration, restoreResult, null, null );

        var moduleCatalog = ModuleCatalog.Create(
            builtInModules,
            configLoader.DiscoverLocalModulePaths( configuration.ConfigRoot ),
            restoreResult.RestoredPlugins
        );
        var roleCatalog = RoleCatalog.Create(
            configLoader.DiscoverLocalRoles( configuration.ConfigRoot ),
            restoreResult.RestoredPlugins
        );
        var roleTasks = configuration
            .RequestedRoles.Select( roleCatalog.Resolve )
            .SelectMany( role => configLoader.LoadRoleTasks( role, configuration.RootVariables ) );
        var tasks = configuration.RootTasks.Concat( roleTasks ).ToArray();

        return new PreparedExecution( configuration, restoreResult, moduleCatalog, tasks );
    }

    private sealed record PreparedExecution(
        LoadedConfiguration Configuration,
        PluginRestoreResult RestoreResult,
        ModuleCatalog? ModuleCatalog,
        IReadOnlyList<LoadedTask>? Tasks
    );
}
