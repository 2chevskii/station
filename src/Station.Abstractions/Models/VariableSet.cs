using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Station.Abstractions.Models;

public sealed class VariableSet
{
    public VariableSet(JsonNode? root)
    {
        Root = root?.DeepClone();
    }

    public JsonNode? Root { get; }

    public VariableSet CreateOverlay(JsonNode? overlay) =>
        new(MergeNodes(Root, overlay));

    public bool Contains(string path) => TryGetNode(path, out _);

    public T? Get<T>(string path)
    {
        if (!TryGetValue<T>(path, out var value))
        {
            throw new ConfigurationException($"Variable '{path}' is not available.");
        }

        return value;
    }

    public bool TryGetValue<T>(string path, out T? value)
    {
        if (!TryGetNode(path, out var node) || node is null)
        {
            value = default;
            return false;
        }

        try
        {
            value = node.Deserialize<T>();
            return true;
        }
        catch (Exception) when (typeof(T) == typeof(string))
        {
            value = (T?)(object?)node.ToJsonString();
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    public bool TryGetNode(string path, out JsonNode? node)
    {
        node = Root;
        foreach (var segment in SplitPath(path))
        {
            if (node is not JsonObject jsonObject)
            {
                node = null;
                return false;
            }

            if (!jsonObject.TryGetPropertyValue(segment, out node))
            {
                var match = jsonObject.FirstOrDefault(pair =>
                    string.Equals(pair.Key, segment, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(match.Key))
                {
                    node = null;
                    return false;
                }

                node = match.Value;
            }
        }

        return node is not null;
    }

    public object? ToObjectGraph() => ConvertNodeToObject(Root);

    public IEnumerable<(string Path, JsonNode? Value)> EnumeratePaths()
    {
        if (Root is null)
        {
            yield break;
        }

        foreach (var item in EnumeratePaths(Root, string.Empty))
        {
            yield return item;
        }
    }

    private static IEnumerable<(string Path, JsonNode? Value)> EnumeratePaths(JsonNode node, string path)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var (key, value) in jsonObject)
            {
                var childPath = string.IsNullOrEmpty(path) ? key : $"{path}.{key}";
                if (value is null)
                {
                    yield return (childPath, null);
                    continue;
                }

                foreach (var item in EnumeratePaths(value, childPath))
                {
                    yield return item;
                }
            }

            yield break;
        }

        yield return (path, node);
    }

    private static IEnumerable<string> SplitPath(string path) =>
        path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JsonNode? MergeNodes(JsonNode? baseNode, JsonNode? overlayNode)
    {
        if (baseNode is null)
        {
            return overlayNode?.DeepClone();
        }

        if (overlayNode is null)
        {
            return baseNode.DeepClone();
        }

        if (baseNode is JsonObject baseObject && overlayNode is JsonObject overlayObject)
        {
            var merged = (JsonObject)baseObject.DeepClone();
            foreach (var (key, value) in overlayObject)
            {
                merged[key] = MergeNodes(merged[key], value);
            }

            return merged;
        }

        return overlayNode.DeepClone();
    }

    private static object? ConvertNodeToObject(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonObject jsonObject => ConvertObject(jsonObject),
            JsonArray jsonArray => jsonArray.Select(ConvertNodeToObject).ToArray(),
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolValue) => boolValue,
            JsonValue jsonValue when jsonValue.TryGetValue<int>(out var intValue) => intValue,
            JsonValue jsonValue when jsonValue.TryGetValue<long>(out var longValue) => longValue,
            JsonValue jsonValue when jsonValue.TryGetValue<decimal>(out var decimalValue) => decimalValue,
            JsonValue jsonValue when jsonValue.TryGetValue<double>(out var doubleValue) => doubleValue,
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue) => stringValue,
            JsonValue jsonValue => jsonValue.ToJsonString(),
            _ => node.ToJsonString(),
        };

    private static IReadOnlyDictionary<string, object?> ConvertObject(JsonObject jsonObject)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in jsonObject)
        {
            result[key] = ConvertNodeToObject(value);
        }

        return result;
    }
}
