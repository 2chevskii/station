using System.Text.Json;
using Station.Abstractions.Models;

namespace Station.Core.Infrastructure.Configuration;

internal static class ConfigValueDisplay
{
    public static string Format(ConfigValue? value)
    {
        return value switch
        {
            null or ConfigNull => "<null>",
            VariableReference reference => $"<ref: {reference.Path}>",
            TemplateMarkup markup => markup.TemplateText,
            ConfigScalar { Kind: ConfigScalarKind.String, Value: string stringValue } =>
                stringValue,
            ConfigScalar scalar => Convert.ToString(
                scalar.Value,
                System.Globalization.CultureInfo.InvariantCulture
            )
                ?? scalar.Value.ToString()
                ?? string.Empty,
            ConfigArray array => JsonSerializer.Serialize(array.Select(FormatObjectGraphValue)),
            ConfigObject configObject => JsonSerializer.Serialize(
                configObject.ToDictionary(
                    static pair => pair.Key,
                    static pair => FormatObjectGraphValue(pair.Value),
                    StringComparer.OrdinalIgnoreCase
                )
            ),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static object? FormatObjectGraphValue(ConfigValue value) =>
        value switch
        {
            ConfigNull => null,
            ConfigScalar scalar => scalar.Value,
            VariableReference reference => $"<ref: {reference.Path}>",
            TemplateMarkup markup => markup.TemplateText,
            ConfigArray array => array.Select(FormatObjectGraphValue).ToArray(),
            ConfigObject configObject => configObject.ToDictionary(
                static pair => pair.Key,
                static pair => FormatObjectGraphValue(pair.Value),
                StringComparer.OrdinalIgnoreCase
            ),
            _ => value.ToString(),
        };
}
