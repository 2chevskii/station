using System.Collections;

namespace Station.Abstractions.Models;

public abstract record ConfigValue;

public sealed record ConfigNull() : ConfigValue
{
    public static readonly ConfigNull Instance = new();
}

public sealed record ConfigScalar(ConfigScalarKind Kind, object Value) : ConfigValue
{
    public static ConfigScalar String(string value) => new(ConfigScalarKind.String, value);

    public static ConfigScalar Boolean(bool value) => new(ConfigScalarKind.Boolean, value);

    public static ConfigScalar Integer(long value) => new(ConfigScalarKind.Integer, value);

    public static ConfigScalar Float(double value) => new(ConfigScalarKind.Float, value);
}

public sealed record ConfigArray : ConfigValue, IReadOnlyList<ConfigValue>
{
    private readonly IReadOnlyList<ConfigValue> items;

    public ConfigArray()
        : this(Array.Empty<ConfigValue>())
    {
    }

    public ConfigArray(IEnumerable<ConfigValue> items)
    {
        this.items = items.Select(ConfigValueUtilities.EnsureValue).ToArray();
    }

    public ConfigValue this[int index] => items[index];

    public int Count => items.Count;

    public IEnumerator<ConfigValue> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record ConfigObject : ConfigValue, IReadOnlyDictionary<string, ConfigValue>
{
    private readonly IReadOnlyDictionary<string, ConfigValue> properties;

    public ConfigObject()
        : this(Array.Empty<KeyValuePair<string, ConfigValue>>())
    {
    }

    public ConfigObject(IEnumerable<KeyValuePair<string, ConfigValue>> properties)
    {
        this.properties = properties.ToDictionary(
            static pair => pair.Key,
            static pair => ConfigValueUtilities.EnsureValue(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    public ConfigValue this[string key] => properties[key];

    public IEnumerable<string> Keys => properties.Keys;

    public IEnumerable<ConfigValue> Values => properties.Values;

    public int Count => properties.Count;

    public bool ContainsKey(string key) => properties.ContainsKey(key);

    public bool TryGetValue(string key, out ConfigValue value)
    {
        if (properties.TryGetValue(key, out var actual))
        {
            value = actual;
            return true;
        }

        value = ConfigNull.Instance;
        return false;
    }

    public IEnumerator<KeyValuePair<string, ConfigValue>> GetEnumerator() =>
        properties.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record VariableReference(string Path) : ConfigValue;

public sealed record TemplateMarkup(string TemplateText) : ConfigValue;
