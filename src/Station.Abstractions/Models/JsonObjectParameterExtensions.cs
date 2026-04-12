using System.Text.Json;
using System.Text.Json.Nodes;

namespace Station.Abstractions.Models;

public static class JsonObjectParameterExtensions
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = false,
    };

    public static string RequireString(this JsonObject parameters, string propertyName)
    {
        if (!parameters.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            throw new ConfigurationException($"Missing required parameter '{propertyName}'.");
        }

        if (value is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var stringValue) &&
            !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue;
        }

        throw new ConfigurationException($"Parameter '{propertyName}' must be a non-empty string.");
    }

    public static string? GetOptionalString(this JsonObject parameters, string propertyName)
    {
        if (!parameters.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue)
            ? stringValue
            : value.ToJsonString(JsonSerializerOptions);
    }

    public static bool GetBoolean(this JsonObject parameters, string propertyName, bool defaultValue = false)
    {
        if (!parameters.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return defaultValue;
        }

        if (value is JsonValue jsonBoolean && jsonBoolean.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        if (value is JsonValue jsonString &&
            jsonString.TryGetValue<string>(out var stringValue) &&
            bool.TryParse(stringValue, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }
}
