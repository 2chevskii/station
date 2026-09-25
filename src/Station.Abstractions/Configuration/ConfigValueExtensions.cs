using System.Text.Json;

namespace Station.Abstractions.Configuration;

public static class ConfigValueExtensions
{
    public static T? GetValue<T>( this ConfigValue value )
    {
        return value switch
        {
            ConfigNull => default(T?),
            ConfigScalar scalar when scalar.Value is T typedValue => typedValue,
            ConfigScalar scalar => JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize( scalar.Value )
            ),
            var _ => JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize(
                    ConfigValueUtilities.ToObjectGraph( value, new VariableSet( new ConfigObject() ) )
                )
            ),
        };
    }
}