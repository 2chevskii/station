using System.Text.Json;
using System.Text.Json.Nodes;

using Station.Abstractions.Modules;

namespace Station.Modules.Builtin.Script;

internal static class ScriptResultParser
{
    public static ModuleResult Parse( string stdout )
    {
        if ( string.IsNullOrWhiteSpace( stdout ) )
            return ModuleResult.Failure( "Script module produced no JSON output." );

        try
        {
            using var document = JsonDocument.Parse( stdout );
            var payload = document.RootElement;
            if ( payload.ValueKind != JsonValueKind.Object )
                throw new JsonException( "The response must be a JSON object." );

            ValidateUniqueProperties( payload );

            return new ModuleResult(
                ReadBoolean( payload, "changed" ),
                ReadBoolean( payload, "failed" ),
                ReadBoolean( payload, "skipped" ),
                ReadMessage( payload ),
                ReadOutputs( payload )
            );
        }
        catch ( JsonException exception )
        {
            return ModuleResult.Failure(
                $"Script module returned invalid JSON: {exception.Message}"
            );
        }
    }

    private static void ValidateUniqueProperties( JsonElement value )
    {
        if ( value.ValueKind == JsonValueKind.Object )
        {
            var names = new HashSet<string>( StringComparer.Ordinal );
            foreach ( var property in value.EnumerateObject() )
            {
                if ( !names.Add( property.Name ) )
                    throw new JsonException(
                        $"Property '{property.Name}' is specified more than once."
                    );

                ValidateUniqueProperties( property.Value );
            }
        }
        else if ( value.ValueKind == JsonValueKind.Array )
        {
            foreach ( var item in value.EnumerateArray() )
                ValidateUniqueProperties( item );
        }
    }

    private static bool ReadBoolean( JsonElement payload, string name )
    {
        if ( !payload.TryGetProperty( name, out var value ) )
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new JsonException( $"Property '{name}' must be a boolean." ),
        };
    }

    private static string? ReadMessage( JsonElement payload )
    {
        if ( !payload.TryGetProperty( "message", out var value )
            || value.ValueKind == JsonValueKind.Null )
            return null;

        if ( value.ValueKind != JsonValueKind.String )
            throw new JsonException( "Property 'message' must be a string or null." );

        return value.GetString();
    }

    private static JsonObject? ReadOutputs( JsonElement payload )
    {
        if ( !payload.TryGetProperty( "outputs", out var value )
            || value.ValueKind == JsonValueKind.Null )
            return null;

        if ( value.ValueKind != JsonValueKind.Object )
            throw new JsonException( "Property 'outputs' must be an object or null." );

        return JsonNode.Parse( value.GetRawText() )!.AsObject();
    }
}
