using Station.Core.Configuration.Artifacts;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Roles;

namespace Station.Core.Configuration.Loading;

internal sealed class VariableScopeLoader
{
    private readonly IConfigArtifactResolver artifactResolver;
    private readonly IReadOnlyDictionary<string, IVariableDocumentLoader> variableLoaders;

    public VariableScopeLoader(
        IConfigArtifactResolver artifactResolver,
        IEnumerable<IVariableDocumentLoader> variableLoaders
    )
    {
        this.artifactResolver = artifactResolver;
        this.variableLoaders = variableLoaders.ToDictionary(
            static loader => loader.Id,
            static loader => loader,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public VariableSet LoadRootVariables(
        string configRoot,
        DiscoveredConfigTree tree,
        IDictionary<string, object?> manifest,
        string manifestPath,
        IReadOnlyDictionary<string, string>? cliVariables
    )
    {
        var rootVariables = new ConfigObject();

        foreach ( var artifact in artifactResolver.EnumerateRootVarArtifacts( tree ) )
        {
            var loader = ResolveLoader( artifact.LoaderId );
            var loaded = loader.LoadVariables(
                artifact,
                new ConfigEvaluationContext(
                    configRoot,
                    artifact,
                    new VariableSet( rootVariables ),
                    null,
                    null
                )
            );
            rootVariables = (ConfigObject)ConfigValueUtilities.Merge( rootVariables, loaded );
        }

        var manifestVariables = ToVariableObject(
            manifest.GetOptionalMapping( "vars" ),
            manifestPath,
            "vars"
        );
        rootVariables = (ConfigObject)ConfigValueUtilities.Merge( rootVariables, manifestVariables );
        rootVariables = (ConfigObject)
            ConfigValueUtilities.Merge( rootVariables, VariableOverrideLoader.LoadEnvironment() );
        rootVariables = (ConfigObject)
            ConfigValueUtilities.Merge( rootVariables, VariableOverrideLoader.LoadCli( cliVariables ) );
        rootVariables = (ConfigObject)
            ConfigValueUtilities.Merge( rootVariables, VariableOverrideLoader.LoadGlobals() );
        return new VariableSet( rootVariables );
    }

    public VariableSet CreateRoleVariables(
        ResolvedRole role,
        VariableSet rootVariables,
        ConfigObject inlineDefaults
    )
    {
        var artifact = artifactResolver.ResolveRoleDefaultsArtifact(
            role.RoleDirectory,
            role.FullName
        );
        var roleDefaults = artifact is null
            ? new ConfigObject()
            : ResolveLoader( artifact.LoaderId )
                .LoadVariables(
                    artifact,
                    new ConfigEvaluationContext(
                        role.RoleRoot,
                        artifact,
                        new VariableSet( new ConfigObject() ),
                        role.ShortName,
                        role.RoleRoot
                    )
                );

        var mergedDefaults = (ConfigObject)ConfigValueUtilities.Merge( roleDefaults, inlineDefaults );
        return new VariableSet(
            (ConfigObject)ConfigValueUtilities.Merge( mergedDefaults, rootVariables.Root )
        );
    }

    private IVariableDocumentLoader ResolveLoader( string loaderId ) =>
        variableLoaders.TryGetValue( loaderId, out var loader )
            ? loader
            : throw new ConfigurationException(
                $"No variable loader is registered for '{loaderId}'."
            );

    private static ConfigObject ToVariableObject(
        object? value,
        string sourceName,
        string? propertyName = null
    )
    {
        if ( value is null )
            return new ConfigObject();

        if ( value is not IDictionary<string, object?> variables )
        {
            var name = propertyName is null ? sourceName : $"{sourceName}:{propertyName}";
            throw new ConfigurationException(
                $"Variables from '{name}' must contain a top-level mapping."
            );
        }

        return ConfigValueFactory.RequireObject(
            ConfigValueFactory.FromClrValue( variables ),
            propertyName is null ? sourceName : $"{sourceName}:{propertyName}"
        );
    }
}
