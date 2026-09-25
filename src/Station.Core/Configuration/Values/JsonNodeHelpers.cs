using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Station.Core.Configuration.Values;

public static class JsonNodeHelpers
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    public static JsonObject ToJsonObject( IDictionary<string, object?> values )
    {
        var result = new JsonObject();
        foreach ( var (key, value) in values )
            result[key] = ToJsonNode( value );

        return result;
    }

    public static JsonNode? ToJsonNode( object? value ) =>
        value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            string stringValue => JsonValue.Create( stringValue ),
            bool boolValue => JsonValue.Create( boolValue ),
            int intValue => JsonValue.Create( intValue ),
            long longValue => JsonValue.Create( longValue ),
            double doubleValue => JsonValue.Create( doubleValue ),
            decimal decimalValue => JsonValue.Create( decimalValue ),
            float floatValue => JsonValue.Create( floatValue ),
            IEnumerable<object?> list => new JsonArray( list.Select( ToJsonNode ).ToArray() ),
            IDictionary<string, object?> dictionary => ToJsonObject( dictionary ),
            var _ => JsonSerializer.SerializeToNode( value, JsonSerializerOptions ),
        };

    public static JsonObject RenderStringScalars(
        JsonObject value,
        VariableSet variables,
        ITemplateEngine templateRenderer,
        string? sourceName = null
    ) =>
        (JsonObject)RenderNode( value, variables, templateRenderer, sourceName )!;

    public static JsonNode? RenderNode(
        JsonNode? node,
        VariableSet variables,
        ITemplateEngine templateRenderer,
        string? sourceName = null
    ) =>
        node switch
        {
            null => null,
            JsonObject jsonObject => new JsonObject(
                jsonObject.Select( pair =>
                    KeyValuePair.Create(
                        pair.Key,
                        RenderNode( pair.Value, variables, templateRenderer, sourceName )
                    )
                )
            ),
            JsonArray jsonArray => new JsonArray(
                jsonArray
                    .Select( value => RenderNode( value, variables, templateRenderer, sourceName ) )
                    .ToArray()
            ),
            JsonValue jsonValue when jsonValue.TryGetValue<string>( out var stringValue ) =>
                JsonValue.Create( templateRenderer.Render( stringValue, variables, sourceName ) ),
            JsonValue jsonValue => jsonValue.DeepClone(),
            var _ => node.DeepClone(),
        };

    public static object? NormalizeYamlValue( object? value ) =>
        value switch
        {
            null => null,
            DateTime dateTime => dateTime.ToString( "O", CultureInfo.InvariantCulture ),
            IDictionary<object, object> dictionary => dictionary.ToDictionary(
                pair =>
                    Convert.ToString( pair.Key, CultureInfo.InvariantCulture )
                    ?? throw new ConfigurationException( "YAML mapping keys must be scalar values." ),
                pair => NormalizeYamlValue( pair.Value ),
                StringComparer.OrdinalIgnoreCase
            ),
            IDictionary<string, object> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeYamlValue( pair.Value ),
                StringComparer.OrdinalIgnoreCase
            ),
            IEnumerable<object?> list when value is not string => list.Select( NormalizeYamlValue )
                .ToList(),
            var _ => value,
        };

    public static JsonObject GetObject( this JsonObject parameters, string propertyName )
    {
        if (
            !parameters.TryGetPropertyValue( propertyName, out var value )
            || value is not JsonObject jsonObject
        )
            throw new ConfigurationException( $"Parameter '{propertyName}' must be an object." );

        return jsonObject;
    }
}