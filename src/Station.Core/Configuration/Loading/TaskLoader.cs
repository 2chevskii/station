using Station.Core.Configuration.Artifacts;
using Station.Core.Configuration.Roles;

namespace Station.Core.Configuration.Loading;

internal sealed class TaskLoader
{
    private readonly IConfigArtifactResolver artifactResolver;
    private readonly IReadOnlyDictionary<string, IRoleDocumentLoader> roleLoaders;
    private readonly TaskDefinitionParser taskParser;

    public TaskLoader(
        TaskDefinitionParser taskParser,
        IConfigArtifactResolver artifactResolver,
        IEnumerable<IRoleDocumentLoader> roleLoaders
    )
    {
        this.taskParser = taskParser;
        this.artifactResolver = artifactResolver;
        this.roleLoaders = roleLoaders.ToDictionary(
            static loader => loader.Id,
            static loader => loader,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public IReadOnlyList<LoadedTask> LoadRootTasks(
        string configRoot,
        IDictionary<string, object?> manifest,
        VariableSet variables,
        string manifestPath
    )
    {
        var taskValues = manifest.GetOptionalList( "tasks" ) ?? [];
        return taskParser.Parse(
            new ConfigArray(
                taskValues.Select( static value => ConfigValueFactory.FromClrValue( value ) )
            ),
            null,
            configRoot,
            null,
            variables,
            manifestPath,
            "station.tasks"
        );
    }

    public IReadOnlyList<LoadedTask> LoadRoleTasks( ResolvedRole role, VariableSet variables )
    {
        var (artifact, loader) = ResolveRoleLoader( role );

        var tasks = loader.LoadRoleTasks(
            artifact,
            new ConfigEvaluationContext(
                role.RoleRoot,
                artifact,
                variables,
                role.ShortName,
                role.RoleRoot
            )
        );

        return taskParser.Parse(
            tasks,
            role.ShortName,
            role.RoleRoot,
            role.RoleRoot,
            variables,
            artifact.FullPath,
            $"{role.ShortName}.tasks"
        );
    }

    public RoleDocumentProperties ReadRoleProperties( ResolvedRole role )
    {
        var (artifact, loader) = ResolveRoleLoader( role );
        return loader.ReadProperties( artifact );
    }

    private (ResolvedConfigArtifact Artifact, IRoleDocumentLoader Loader) ResolveRoleLoader(
        ResolvedRole role
    )
    {
        var artifact = artifactResolver.ResolveRoleTaskArtifact( role.RoleDirectory, role.FullName );
        if ( !roleLoaders.TryGetValue( artifact.LoaderId, out var loader ) )
            throw new ConfigurationException(
                $"No role loader is registered for '{artifact.DisplayName}'."
            );

        return (artifact, loader);
    }
}
