using System.Text.Json;

namespace Station.Abstractions.Configuration;

public static class ConfigValueExtensions
{
    public static T? GetValue<T>(this ConfigValue value)
    {
        return value switch
        {
            ConfigNull => default,
            ConfigScalar scalar when scalar.Value is T typedValue => typedValue,
            ConfigScalar scalar => JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize(scalar.Value)
            ),
            _ => JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize(
                    ConfigValueUtilities.ToObjectGraph(value, new VariableSet(new ConfigObject()))
                )
            ),
        };
    }
}
