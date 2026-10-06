using System.Collections;
using NLua;
using Station.Abstractions.Configuration;
using Station.Configuration.Abstractions.Values;

namespace Station.Configuration.Lua;

internal sealed class LuaValueConverter(LuaFunction isArray)
{
    private const int MaximumDepth = 64;
    private readonly List<LuaTable> ancestors = [];

    public ConfigValue ConvertValue(object? value)
    {
        if (value is not LuaTable table)
            return ConfigValueFactory.FromClrValue(value);

        return ConvertTable(table, false);
    }

    public ConfigArray ConvertArray(LuaTable table) => (ConfigArray)ConvertTable(table, true);

    private ConfigValue ConvertTable(LuaTable table, bool requireArray)
    {
        // LuaTable equality compares Lua identities; its hash code uses the wrapper reference.
        if (ancestors.Any(ancestor => ancestor.Equals(table)))
            throw new ConfigurationException("Lua configuration contains a cyclic table.");

        if (ancestors.Count >= MaximumDepth)
            throw new ConfigurationException("Lua configuration tables are nested too deeply.");

        ancestors.Add(table);
        try
        {
            var entries = new List<DictionaryEntry>();
            var enumerator = table.GetEnumerator();
            while (enumerator.MoveNext())
                entries.Add(enumerator.Entry);

            var markedArray = isArray.Call(table)[0] is true;
            if (requireArray || markedArray || entries.Any(entry => entry.Key is not string))
                return ReadArray(entries);

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var key = (string)entry.Key;
                if (!values.TryAdd(key, ConvertValue(entry.Value)))
                    throw new ConfigurationException(
                        $"Lua configuration contains duplicate mapping key '{key}'."
                    );
            }

            // Preserve the shared variable-reference contract before converting a mapping.
            if (
                values.TryGetValue("$type", out var type)
                && type is ConfigScalar { Value: "variable_reference" }
                && values.TryGetValue("name", out var name)
                && name is ConfigScalar { Value: string path }
                && !string.IsNullOrWhiteSpace(path)
            )
                return new VariableReference(path);

            return ConfigValueFactory.FromClrValue(values);
        }
        finally
        {
            ancestors.RemoveAt(ancestors.Count - 1);
        }
    }

    private ConfigArray ReadArray(IReadOnlyList<DictionaryEntry> entries)
    {
        var values = new ConfigValue[entries.Count];
        foreach (var entry in entries)
        {
            if (entry.Key is not long index || index < 1 || index > entries.Count)
                throw new ConfigurationException(
                    "Lua arrays must have consecutive integer keys starting at 1; mixed and sparse tables are unsupported."
                );

            values[index - 1] = ConvertValue(entry.Value);
        }
        return new ConfigArray(values);
    }
}
