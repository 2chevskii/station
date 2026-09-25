using System.Text.Json;
using Station.Abstractions.Interfaces;

namespace Station.Abstractions.Models;

public sealed class VariableSet
{
    public VariableSet(ConfigObject? root)
    {
        Root = root ?? new ConfigObject();
    }

    public ConfigObject Root { get; }

    public VariableSet CreateOverlay(ConfigObject? overlay) =>
        overlay is null
            ? new VariableSet(Root)
            : new((ConfigObject)ConfigValueUtilities.Merge(Root, overlay));

    public bool Contains(string path) => TryGetRaw(path, out _);

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
        if (!TryGetRaw(path, out var rawValue))
        {
            value = default;
            return false;
        }

        var resolved = ConfigValueUtilities.ResolveValue(rawValue, this);
        try
        {
            value = ConvertTo<T>(resolved);
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    public bool TryGetRaw(string path, out ConfigValue value)
    {
        ConfigValue current = Root;
        foreach (var segment in ConfigValueUtilities.SplitPath(path))
        {
            if (
                current is not ConfigObject configObject
                || !configObject.TryGetValue(segment, out current)
            )
            {
                value = ConfigNull.Instance;
                return false;
            }
        }

        value = current;
        return true;
    }

    public object? ToObjectGraph(
        ITemplateEngine? templateEngine = null,
        string? sourceName = null
    ) => ConfigValueUtilities.ToObjectGraph(Root, this, templateEngine, sourceName);

    public IEnumerable<(string Path, ConfigValue Value)> EnumeratePaths() =>
        ConfigValueUtilities
            .EnumeratePaths(Root)
            .Where(static item => !string.IsNullOrWhiteSpace(item.Path));

    private static T? ConvertTo<T>(ConfigValue value)
    {
        if (value is ConfigNull)
        {
            return default;
        }

        var objectGraph = ConfigValueUtilities.ToObjectGraph(
            value,
            new VariableSet(new ConfigObject())
        );
        if (objectGraph is null)
        {
            return default;
        }

        if (objectGraph is T typedValue)
        {
            return typedValue;
        }

        var json = JsonSerializer.Serialize(objectGraph);
        return JsonSerializer.Deserialize<T>(json);
    }
}
