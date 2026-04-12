using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Station.Abstractions.Enums;
using Station.Core.Execution.Models;
using Station.Core.Infrastructure.Json;

namespace Station.Core.Execution.Services;

internal sealed class ModuleBinder
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ModuleBindingResult Bind(
        ModuleSchema schema,
        ModuleContext context,
        JsonObject rawParameters,
        bool allowDeferred)
    {
        var diagnostics = new List<ModuleDiagnostic>();
        var boundParameters = new List<BoundParameterRecord>();
        var rawEntries = rawParameters.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase
        );
        var usedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var module = (ConfigModule)schema.Constructor.Invoke([context]);

        foreach (var parameter in schema.Parameters)
        {
            var matches = rawEntries
                .Where(entry =>
                    string.Equals(entry.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)
                    || parameter.Aliases.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length > 1)
            {
                diagnostics.Add(
                    new ModuleDiagnostic(
                        $"Parameter '{parameter.Name}' is specified more than once ({string.Join(", ", matches.Select(static match => match.Key))})."
                    )
                );
                continue;
            }

            if (matches.Length == 0)
            {
                if (parameter.Required)
                {
                    diagnostics.Add(
                        new ModuleDiagnostic($"Missing required parameter '{parameter.Name}'.")
                    );
                    continue;
                }

                boundParameters.Add(
                    new BoundParameterRecord(
                        parameter.Name,
                        FormatValue(parameter.Property.GetValue(module)),
                        IsDeferred: false,
                        IsDefaultValue: true,
                        SourceName: null
                    )
                );
                continue;
            }

            var match = matches[0];
            usedEntries.Add(match.Key);
            var binding = BindValue(
                parameter.Property.PropertyType,
                match.Value,
                context.Variables,
                context.TemplateEngine,
                match.Key,
                allowDeferred
            );

            if (binding.Diagnostic is not null)
            {
                diagnostics.Add(binding.Diagnostic);
            }

            if (binding.Success)
            {
                parameter.Property.SetValue(module, binding.Value);
            }

            boundParameters.Add(
                new BoundParameterRecord(
                    parameter.Name,
                    binding.DisplayValue,
                    binding.IsDeferred,
                    IsDefaultValue: false,
                    SourceName: match.Key
                )
            );
        }

        foreach (var entry in rawEntries.Keys.Where(key => !usedEntries.Contains(key)))
        {
            diagnostics.Add(new ModuleDiagnostic($"Unknown parameter '{entry}'."));
        }

        return new ModuleBindingResult(
            diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? null : module,
            diagnostics,
            boundParameters,
            boundParameters.Any(static parameter => parameter.IsDeferred)
        );
    }

    private static (bool Success, object? Value, bool IsDeferred, string DisplayValue, ModuleDiagnostic? Diagnostic) BindValue(
        Type targetType,
        JsonNode? rawValue,
        VariableSet variables,
        Station.Abstractions.Interfaces.ITemplateEngine templateEngine,
        string parameterName,
        bool allowDeferred)
    {
        try
        {
            var renderedNode = JsonNodeHelpers.RenderNode(rawValue, variables, templateEngine, parameterName);
            var convertedValue = ConvertValue(renderedNode, targetType);
            return (true, convertedValue, false, FormatValue(convertedValue), null);
        }
        catch (ConfigurationException exception) when (allowDeferred && exception.Message.Contains("render template", StringComparison.OrdinalIgnoreCase))
        {
            return (
                false,
                null,
                true,
                "<deferred>",
                new ModuleDiagnostic(
                    $"Parameter '{parameterName}' is not fully known before execution: {exception.Message}",
                    DiagnosticSeverity.Warning
                )
            );
        }
        catch (Exception exception) when (exception is ConfigurationException or FormatException or InvalidOperationException or JsonException)
        {
            return (
                false,
                null,
                false,
                "<invalid>",
                new ModuleDiagnostic(
                    $"Parameter '{parameterName}' could not be bound: {exception.Message}"
                )
            );
        }
    }

    private static object? ConvertValue(JsonNode? value, Type targetType)
    {
        if (targetType == typeof(JsonNode) || targetType.IsAssignableFrom(typeof(JsonNode)))
        {
            return value?.DeepClone();
        }

        if (value is null)
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
            if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            throw new ConfigurationException($"Expected a string but received '{value.GetType().Name}'.");
        }

        if (value is JsonValue scalarValue && scalarValue.TryGetValue<string>(out var renderedString))
        {
            return ConvertScalarString(renderedString, targetType);
        }

        return JsonSerializer.Deserialize(value.ToJsonString(), targetType, JsonSerializerOptions)
            ?? throw new ConfigurationException($"Failed to deserialize value for type '{targetType.Name}'.");
    }

    private static object? ConvertScalarString(string value, Type targetType)
    {
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableType == typeof(string))
        {
            return value;
        }

        if (nonNullableType.IsEnum)
        {
            return Enum.Parse(nonNullableType, value, ignoreCase: true);
        }

        if (nonNullableType == typeof(Guid))
        {
            return Guid.Parse(value);
        }

        if (nonNullableType == typeof(DateTime))
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture);
        }

        if (nonNullableType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        if (nonNullableType == typeof(TimeSpan))
        {
            return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
        }

        if (typeof(IConvertible).IsAssignableFrom(nonNullableType))
        {
            return Convert.ChangeType(value, nonNullableType, CultureInfo.InvariantCulture);
        }

        return JsonSerializer.Deserialize(JsonSerializer.Serialize(value), targetType, JsonSerializerOptions)
            ?? throw new ConfigurationException($"Failed to deserialize scalar value for type '{targetType.Name}'.");
    }

    private static string FormatValue(object? value) =>
        value switch
        {
            null => "<null>",
            string stringValue => stringValue,
            JsonNode jsonNode => jsonNode.ToJsonString(),
            _ => JsonSerializer.Serialize(value, JsonSerializerOptions)
        };
}
