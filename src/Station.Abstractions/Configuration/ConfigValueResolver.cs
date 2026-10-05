using Station.Abstractions.Artifacts;

namespace Station.Abstractions.Configuration;

internal static class ConfigValueResolver
{
    public static ConfigValue Resolve(
        ConfigValue value,
        VariableSet variables,
        ITemplateEngine? templateEngine,
        string? sourceName
    ) => ResolveValue( value, variables, templateEngine, sourceName, [] );

    private static ConfigValue ResolveValue(
        ConfigValue value,
        VariableSet variables,
        ITemplateEngine? templateEngine,
        string? sourceName,
        List<string> resolutionStack
    )
    {
        return value switch
        {
            ConfigNull => ConfigNull.Instance,
            ConfigScalar scalar => scalar,
            VariableReference reference => ResolveReference(
                reference,
                variables,
                templateEngine,
                sourceName,
                resolutionStack
            ),
            TemplateMarkup markup => ResolveTemplate( markup, variables, templateEngine, sourceName ),
            ConfigArray array => new ConfigArray(
                array.Select( item =>
                    ResolveValue( item, variables, templateEngine, sourceName, resolutionStack )
                )
            ),
            ConfigObject configObject => new ConfigObject(
                configObject.Select( pair =>
                    KeyValuePair.Create(
                        pair.Key,
                        ResolveValue(
                            pair.Value,
                            variables,
                            templateEngine,
                            sourceName,
                            resolutionStack
                        )
                    )
                )
            ),
            var _ => throw new ConfigurationException(
                $"Unsupported configuration value '{value.GetType().Name}'."
            ),
        };
    }

    private static ConfigValue ResolveReference(
        VariableReference reference,
        VariableSet variables,
        ITemplateEngine? templateEngine,
        string? sourceName,
        List<string> resolutionStack
    )
    {
        if ( resolutionStack.Contains( reference.Path, StringComparer.OrdinalIgnoreCase ) )
        {
            var cycle = string.Join( " -> ", resolutionStack.Append( reference.Path ) );
            throw new ConfigurationException( $"Variable reference cycle detected: {cycle}." );
        }

        if ( !variables.TryGetRaw( reference.Path, out var target ) )
            throw new ConfigurationException( $"Variable '{reference.Path}' is not available." );

        resolutionStack.Add( reference.Path );
        try
        {
            return ResolveValue( target, variables, templateEngine, sourceName, resolutionStack );
        }
        finally
        {
            resolutionStack.RemoveAt( resolutionStack.Count - 1 );
        }
    }

    private static ConfigValue ResolveTemplate(
        TemplateMarkup markup,
        VariableSet variables,
        ITemplateEngine? templateEngine,
        string? sourceName
    )
    {
        if ( templateEngine is null )
            return ConfigScalar.String( markup.TemplateText );

        return ConfigScalar.String(
            templateEngine.Render( markup.TemplateText, variables, sourceName )
        );
    }
}
