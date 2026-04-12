using System.Text.Json;
using SharpYaml;
using Station.Core.Infrastructure.Json;

namespace Station.Core.Configuration.Services;

internal sealed class YamlConfigurationReader
{
    public object? LoadObject(string path)
    {
        using var reader = File.OpenText(path);
        return JsonNodeHelpers.NormalizeYamlValue(
            YamlSerializer.Deserialize<object?>(reader, CreateYamlOptions(path))
        );
    }

    public T Load<T>(string path)
    {
        using var reader = File.OpenText(path);
        return YamlSerializer.Deserialize<T>(reader, CreateYamlOptions(path))!;
    }

    private static YamlSerializerOptions CreateYamlOptions(string path) =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            SourceName = path,
        };
}
