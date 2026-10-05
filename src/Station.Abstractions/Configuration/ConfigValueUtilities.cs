using System.Text.Json;
using System.Text.Json.Nodes;

using Station.Abstractions.Artifacts;

namespace Station.Abstractions.Configuration;

public static class ConfigValueUtilities
{
    public static ConfigValue EnsureValue( ConfigValue? value ) => value ?? ConfigNull.Instance;

    public static bool IsTemplateMarkup( string value ) =>
        value.Contains( "{{", StringComparison.Ordinal )
        || value.Contains( "{%", StringComparison.Ordinal );

    public static IEnumerable<string> SplitPath( string path ) =>
        path.Split( '.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

    public static ConfigValue Merge( ConfigValue? current, ConfigValue? incoming )
    {
        var effectiveCurrent = EnsureValue( current );
        var effectiveIncoming = EnsureValue( incoming );

        if (
            effectiveCurrent is ConfigObject currentObject
            && effectiveIncoming is ConfigObject incomingObject
        )
        {
            var merged = currentObject.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase
            );

            foreach ( var (key, value) in incomingObject )
                merged[key] = Merge(
                    merged.TryGetValue( key, out var existing ) ? existing : null,
                    value
                );

            return new ConfigObject( merged );
        }

        return effectiveIncoming;
    }

    public static ConfigValue ResolveValue(
        ConfigValue? value,
        VariableSet variables,
        ITemplateEngine? templateEngine = null,
        string? sourceName = null
    ) =>
        ConfigValueResolver.Resolve( EnsureValue( value ), variables, templateEngine, sourceName );

    public static object? ToObjectGraph(
        ConfigValue? value,
        VariableSet variables,
        ITemplateEngine? templateEngine = null,
        string? sourceName = null
    ) =>
        ConvertToObjectGraph( ResolveValue( value, variables, templateEngine, sourceName ) );

    public static JsonNode? ToJsonNode(
        ConfigValue? value,
        VariableSet variables,
        ITemplateEngine? templateEngine = null,
        string? sourceName = null
    ) =>
        JsonSerializer.SerializeToNode(
            ToObjectGraph( value, variables, templateEngine, sourceName )
        );

    public static IEnumerable<(string Path, ConfigValue Value)> EnumeratePaths(
        ConfigValue? value,
        string path = ""
    )
    {
        if ( value is null || value is ConfigNull )
        {
            yield return (path, ConfigNull.Instance);

            yield break;
        }

        if ( value is ConfigObject configObject )
        {
            foreach ( var (key, child) in configObject )
            {
                var childPath = string.IsNullOrEmpty( path ) ? key : $"{path}.{key}";
                foreach ( var item in EnumeratePaths( child, childPath ) )
                    yield return item;
            }

            yield break;
        }

        yield return (path, value);
    }

    private static object? ConvertToObjectGraph( ConfigValue value ) =>
        value switch
        {
            ConfigNull => null,
            ConfigScalar scalar => scalar.Value,
            ConfigArray array => array.Select( ConvertToObjectGraph ).ToArray(),
            ConfigObject configObject => configObject.ToDictionary(
                static pair => pair.Key,
                static pair => ConvertToObjectGraph( pair.Value ),
                StringComparer.OrdinalIgnoreCase
            ),
            VariableReference reference => reference.Path,
            TemplateMarkup markup => markup.TemplateText,
            var _ => throw new ConfigurationException(
                $"Unsupported configuration value '{value.GetType().Name}'."
            ),
        };
}