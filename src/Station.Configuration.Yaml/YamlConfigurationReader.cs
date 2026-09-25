using System.Globalization;
using System.Text.Json;
using SharpYaml;
using Station.Abstractions.Models;
using TruePath;
using TruePath.SystemIo;

namespace Station.Configuration.Yaml;

public sealed class YamlConfigurationReader
{
    public object? LoadObject(string path)
    {
        using var reader = new AbsolutePath(path).OpenText();
        return NormalizeYamlValue(
            YamlSerializer.Deserialize<object?>(reader, CreateYamlOptions(path))
        );
    }

    public T Load<T>(string path)
    {
        using var reader = new AbsolutePath(path).OpenText();
        return YamlSerializer.Deserialize<T>(reader, CreateYamlOptions(path))!;
    }

    private static object? NormalizeYamlValue(object? value) =>
        value switch
        {
            null => null,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            IDictionary<object, object> dictionary => dictionary.ToDictionary(
                pair =>
                    Convert.ToString(pair.Key, CultureInfo.InvariantCulture)
                    ?? throw new ConfigurationException("YAML mapping keys must be scalar values."),
                pair => NormalizeYamlValue(pair.Value),
                StringComparer.OrdinalIgnoreCase
            ),
            IDictionary<string, object> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeYamlValue(pair.Value),
                StringComparer.OrdinalIgnoreCase
            ),
            IEnumerable<object?> list when value is not string => list.Select(NormalizeYamlValue)
                .ToList(),
            _ => value,
        };

    private static YamlSerializerOptions CreateYamlOptions(string path) =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            SourceName = path,
        };
}
