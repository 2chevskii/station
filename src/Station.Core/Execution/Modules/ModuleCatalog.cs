using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Plugins;

using TruePath;

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
        IEnumerable<ModuleRegistration> builtInModules,
        IEnumerable<string> localModulePaths,
        IEnumerable<ResolvedPluginManifest> plugins
    )
    {
        var resolvedModules = new List<ResolvedModule>();
        var loader = new ModuleLoader();

        foreach ( var registration in builtInModules )
            resolvedModules.Add( loader.LoadBuiltin( registration ) );

        foreach ( var modulePath in localModulePaths )
        {
            var shortName = modulePath.AsLocalPath().GetFilenameWithoutExtension();
            resolvedModules.Add(
                loader.LoadAssembly(
                    modulePath,
                    "station.local",
                    expectedName: shortName
                )
            );
        }

        foreach ( var plugin in plugins )
        foreach ( var module in plugin.Modules )
            resolvedModules.Add(
                loader.LoadAssembly(
                    module.AssemblyPath,
                    plugin.Name,
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
}
