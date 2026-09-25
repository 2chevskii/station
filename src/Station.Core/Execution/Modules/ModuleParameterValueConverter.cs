using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Station.Core.Execution.Modules;

internal static class ModuleParameterValueConverter
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static object? ConvertValue(ConfigValue value, Type targetType)
    {
        if (targetType == typeof(ConfigValue) || targetType.IsAssignableFrom(typeof(ConfigValue)))
        {
            return value;
        }

        if (targetType.IsAssignableTo(typeof(JsonNode)))
        {
            return ConfigValueUtilities.ToJsonNode(value, new VariableSet(new ConfigObject()));
        }

        if (value is ConfigNull)
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
            {
                return null;
            }

            throw new ConfigurationException($"Value cannot be null for type '{targetType.Name}'.");
        }

        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableType == typeof(string))
        {
            if (value is ConfigScalar { Kind: ConfigScalarKind.String, Value: string stringValue })
            {
                return stringValue;
            }

            throw new ConfigurationException(
                $"Expected a string but received '{value.GetType().Name}'."
            );
        }

        if (value is ConfigScalar scalar)
        {
            return ConvertScalarValue(scalar, targetType);
        }

        return JsonSerializer.Deserialize(
                JsonSerializer.Serialize(
                    ConfigValueUtilities.ToObjectGraph(value, new VariableSet(new ConfigObject()))
                ),
                targetType,
                JsonSerializerOptions
            )
            ?? throw new ConfigurationException(
                $"Failed to deserialize value for type '{targetType.Name}'."
            );
    }

    private static object ConvertScalarValue(ConfigScalar value, Type targetType)
    {
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value.Kind == ConfigScalarKind.String && value.Value is string stringValue)
        {
            if (nonNullableType == typeof(string))
            {
                return stringValue;
            }

            if (nonNullableType.IsEnum)
            {
                return Enum.Parse(nonNullableType, stringValue, ignoreCase: true);
            }

            if (nonNullableType == typeof(Guid))
            {
                return Guid.Parse(stringValue);
            }

            if (nonNullableType == typeof(DateTime))
            {
                return DateTime.Parse(stringValue, CultureInfo.InvariantCulture);
            }

            if (nonNullableType == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(stringValue, CultureInfo.InvariantCulture);
            }

            if (nonNullableType == typeof(TimeSpan))
            {
                return TimeSpan.Parse(stringValue, CultureInfo.InvariantCulture);
            }
        }

        if (typeof(IConvertible).IsAssignableFrom(nonNullableType))
        {
            return Convert.ChangeType(value.Value, nonNullableType, CultureInfo.InvariantCulture);
        }

        return JsonSerializer.Deserialize(
                JsonSerializer.Serialize(value.Value),
                targetType,
                JsonSerializerOptions
            )
            ?? throw new ConfigurationException(
                $"Failed to deserialize scalar value for type '{targetType.Name}'."
            );
    }

    public static string FormatValue(object? value) =>
        value switch
        {
            null => "<null>",
            string stringValue => stringValue,
            ConfigValue configValue => ConfigValueDisplay.Format(configValue),
            _ => JsonSerializer.Serialize(value, JsonSerializerOptions),
        };
}
