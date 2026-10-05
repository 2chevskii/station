using System.Globalization;

namespace Station.Core.Configuration.Loading;

internal sealed class TaskDefinitionParser( ITemplateEngine templateEngine )
{
    public IReadOnlyList<LoadedTask> Parse(
        ConfigArray taskNodes,
        string? roleName,
        string taskRoot,
        string? roleRoot,
        VariableSet variables,
        string sourceName,
        string defaultNamePrefix
    )
    {
        var tasks = new List<LoadedTask>();
        for ( var index = 0; index < taskNodes.Count; index++ )
        {
            if ( taskNodes[index] is not ConfigObject taskMapping )
                throw new ConfigurationException(
                    $"Task #{index + 1} in '{sourceName}' must be a mapping."
                );

            var renderedTask = ParseTask(
                taskMapping,
                variables,
                sourceName,
                defaultNamePrefix,
                index + 1
            );
            tasks.Add(
                new LoadedTask(
                    renderedTask.Name,
                    renderedTask.ModuleName,
                    renderedTask.Parameters,
                    taskRoot,
                    roleName,
                    roleRoot,
                    variables
                )
            );
        }

        return tasks;
    }

    private (string Name, string ModuleName, ConfigObject Parameters) ParseTask(
        ConfigObject taskMapping,
        VariableSet variables,
        string sourceName,
        string defaultNamePrefix,
        int oneBasedIndex
    )
    {
        ConfigValue? explicitName = null;
        KeyValuePair<string, ConfigValue>? moduleEntry = null;

        foreach ( var (key, value) in taskMapping )
        {
            if ( string.Equals( key, "name", StringComparison.OrdinalIgnoreCase ) )
            {
                explicitName = value;
                continue;
            }

            if ( moduleEntry is not null )
                throw new ConfigurationException(
                    $"Task in '{sourceName}' must declare exactly one module key besides 'name'."
                );

            moduleEntry = KeyValuePair.Create( key, value );
        }

        if ( moduleEntry is null )
            throw new ConfigurationException( $"Task in '{sourceName}' does not declare a module." );

        var moduleName = moduleEntry.Value.Key.Trim();
        if ( string.IsNullOrWhiteSpace( moduleName ) )
            throw new ConfigurationException(
                $"Task in '{sourceName}' declares an empty module name."
            );

        if ( moduleEntry.Value.Value is not ConfigObject rawParameters )
            throw new ConfigurationException(
                $"Task module '{moduleName}' in '{sourceName}' must map to an object for now."
            );

        var taskName = explicitName is null
            ? templateEngine.Render( $"{defaultNamePrefix}.{oneBasedIndex}", variables, sourceName )
            : RenderTaskName( explicitName, variables, sourceName );

        return (taskName, moduleName, rawParameters);
    }

    private string RenderTaskName( ConfigValue nameValue, VariableSet variables, string sourceName )
    {
        var resolvedName = ConfigValueUtilities.ResolveValue(
            nameValue,
            variables,
            templateEngine,
            sourceName
        );
        return resolvedName switch
        {
            ConfigScalar { Kind: ConfigScalarKind.String, Value: string stringValue } =>
                stringValue,
            ConfigScalar scalar => Convert.ToString( scalar.Value, CultureInfo.InvariantCulture )
                                   ?? throw new ConfigurationException(
                                       $"Task name in '{sourceName}' must resolve to a non-empty scalar."
                                   ),
            var _ => throw new ConfigurationException(
                $"Task name in '{sourceName}' must resolve to a scalar."
            ),
        };
    }
}
