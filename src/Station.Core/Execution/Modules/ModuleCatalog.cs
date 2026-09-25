using System.Reflection;
using System.Runtime.Loader;

using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Plugins;
using Station.Modules.Builtin.Apt;
using Station.Modules.Builtin.Dotfile;
using Station.Modules.Builtin.Scoop;
using Station.Modules.Builtin.Script;
using Station.Modules.Builtin.Winget;

using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Execution.Modules;

internal sealed class ModuleCatalog
{
    private readonly IReadOnlyDictionary<string, ResolvedModule> modulesByFullName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ResolvedModule>> modulesByShortName;

    private ModuleCatalog(
        IReadOnlyDictionary<string, ResolvedModule> modulesByFullName,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedModule>> modulesByShortName
    )
    {
        this.modulesByFullName = modulesByFullName;
        this.modulesByShortName = modulesByShortName;
    }

    public static ModuleCatalog Create(
        IEnumerable<ModuleRegistration>? additionalBuiltInModules,
        IEnumerable<string> localModulePaths,
        IEnumerable<ResolvedPluginManifest> plugins
    )
    {
        var resolvedModules = new List<ResolvedModule>();
        var loadedAssemblies = new Dictionary<string, Assembly>( StringComparer.Ordinal );

        foreach ( var registration in CreateDefaultBuiltIns().Concat( additionalBuiltInModules ?? [] ) )
        {
            var schema = ModuleSchemaRegistry.Get( registration.ModuleType );
            ValidateShortName( schema.Name, $"built-in module '{schema.Name}'" );

            resolvedModules.Add(
                new ResolvedModule(
                    registration.ProviderName,
                    schema.Name,
                    $"{registration.ProviderName}.{schema.Name}",
                    schema
                )
            );
        }

        foreach ( var modulePath in localModulePaths )
        {
            var shortName = modulePath.AsLocalPath().GetFilenameWithoutExtension();
            resolvedModules.Add(
                LoadModuleFromAssembly(
                    modulePath,
                    "station.local",
                    loadedAssemblies,
                    expectedName: shortName
                )
            );
        }

        foreach ( var plugin in plugins )
        foreach ( var module in plugin.Modules )
            resolvedModules.Add(
                LoadModuleFromAssembly(
                    module.AssemblyPath,
                    plugin.Name,
                    loadedAssemblies,
                    module.TypeName,
                    module.ExpectedName
                )
            );

        var duplicateModule = resolvedModules
            .GroupBy( module => module.FullName, StringComparer.OrdinalIgnoreCase )
            .FirstOrDefault( group => group.Count() > 1 );
        if ( duplicateModule is not null )
            throw new ConfigurationException(
                $"Module '{duplicateModule.Key}' is exported more than once."
            );

        var modulesByFullName = resolvedModules.ToDictionary(
            module => module.FullName,
            StringComparer.OrdinalIgnoreCase
        );
        var modulesByShortName = resolvedModules
            .GroupBy( module => module.ShortName, StringComparer.OrdinalIgnoreCase )
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ResolvedModule>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase
            );

        return new ModuleCatalog( modulesByFullName, modulesByShortName );
    }

    private static IReadOnlyList<ModuleRegistration> CreateDefaultBuiltIns() =>
    [
        new ModuleRegistration( "station.builtin", typeof( AptModule ) ),
        new ModuleRegistration( "station.builtin", typeof( CopyModule ) ),
        new ModuleRegistration( "station.builtin", typeof( TemplateModule ) ),
        new ModuleRegistration( "station.builtin", typeof( ScriptModule ) ),
        new ModuleRegistration( "station.builtin", typeof( ScoopModule ) ),
        new ModuleRegistration( "station.builtin", typeof( WingetModule ) ),
    ];

    public IReadOnlyList<ResolvedModule> All =>
        modulesByFullName
            .Values.OrderBy( module => module.FullName, StringComparer.OrdinalIgnoreCase )
            .ToArray();

    public ResolvedModule Resolve( string name )
    {
        if ( modulesByFullName.TryGetValue( name, out var fullNameMatch ) )
            return fullNameMatch;

        if ( !modulesByShortName.TryGetValue( name, out var shortNameMatches ) )
            throw new ConfigurationException( $"Module '{name}' is not registered." );

        if ( shortNameMatches.Count > 1 )
            throw new ConfigurationException(
                $"Module '{name}' is ambiguous. Use one of: {string.Join( ", ", shortNameMatches.Select( static module => module.FullName ) )}."
            );

        return shortNameMatches[0];
    }

    private static ResolvedModule LoadModuleFromAssembly(
        string assemblyPath,
        string providerName,
        IDictionary<string, Assembly> loadedAssemblies,
        string? typeName = null,
        string? expectedName = null
    )
    {
        var resolvedPath = assemblyPath.AsLocalPath().ResolveToCurrentDirectory();
        if ( !resolvedPath.ExistsFile() )
            throw new ConfigurationException( $"Module assembly '{assemblyPath}' does not exist." );

        if ( !loadedAssemblies.TryGetValue( resolvedPath.Value, out var assembly ) )
        {
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath( resolvedPath.Value );
            loadedAssemblies.Add( resolvedPath.Value, assembly );
        }

        var discoveredModules = assembly
            .GetExportedTypes()
            .Where( static type =>
                type is { IsAbstract: false, IsInterface: false }
                && typeof( ConfigModule ).IsAssignableFrom( type )
            )
            .ToArray();

        Type moduleType;
        if ( typeName is not null )
            moduleType =
                discoveredModules.SingleOrDefault( type =>
                    string.Equals( type.FullName, typeName, StringComparison.Ordinal )
                )
                ?? throw new ConfigurationException(
                    $"Module assembly '{assemblyPath}' does not contain public module type '{typeName}'."
                );
        else if ( discoveredModules.Length == 1 )
            moduleType = discoveredModules[0];
        else
            throw new ConfigurationException(
                $"Module assembly '{assemblyPath}' must contain exactly one public {nameof( ConfigModule )} implementation."
            );

        var schema = ModuleSchemaRegistry.Get( moduleType );
        var shortName = schema.Name;
        ValidateShortName( shortName, $"module '{shortName}'" );

        if (
            expectedName is not null
            && !string.Equals( shortName, expectedName, StringComparison.OrdinalIgnoreCase )
        )
            throw new ConfigurationException(
                $"Module assembly '{assemblyPath}' is expected to export '{expectedName}', but its descriptor name is '{shortName}'."
            );

        return new ResolvedModule(
            providerName,
            shortName,
            $"{providerName}.{shortName}",
            schema,
            resolvedPath.Value
        );
    }

    private static void ValidateShortName( string shortName, string owner )
    {
        if (
            string.IsNullOrWhiteSpace( shortName )
            || shortName.Contains( '.' )
            || shortName.AsLocalPath().FileName != shortName
        )
            throw new ConfigurationException( $"{owner} must have a valid short name." );
    }
}