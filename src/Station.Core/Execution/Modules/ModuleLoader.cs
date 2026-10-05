using System.Reflection;
using System.Runtime.Loader;

using Station.Abstractions.FileSystem;

using TruePath.SystemIo;

namespace Station.Core.Execution.Modules;

internal sealed class ModuleLoader
{
    private readonly Dictionary<string, Assembly> loadedAssemblies = new Dictionary<string, Assembly>(
        StringComparer.Ordinal
    );

    public ResolvedModule LoadBuiltin( ModuleRegistration registration )
    {
        var schema = ModuleSchemaRegistry.Get( registration.ModuleType );
        ValidateShortName( schema.Name, $"built-in module '{schema.Name}'" );

        return new ResolvedModule(
            registration.ProviderName,
            schema.Name,
            $"{registration.ProviderName}.{schema.Name}",
            schema
        );
    }

    public ResolvedModule LoadAssembly(
        string assemblyPath,
        string providerName,
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
