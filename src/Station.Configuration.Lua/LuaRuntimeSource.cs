using System.Text;
using Station.Abstractions.Configuration;

namespace Station.Configuration.Lua;

internal static class LuaRuntimeSource
{
    public const string Bootstrap = """
        local tasks = {}
        local arrayMetatable = { __metatable = 'station.array' }
        local getmetatable = getmetatable
        local writeDiagnostic = __station_print
        __station_print = nil

        function array(...)
            return setmetatable({ ... }, arrayMetatable)
        end

        local referenceMetatable = {
            __tostring = function(value) return '{{ ' .. value.name .. ' }}' end,
            __concat = function(left, right) return tostring(left) .. tostring(right) end
        }

        function ref(path)
            if type(path) ~= 'string' or path == '' then
                error('ref(path) requires a nonempty variable path')
            end
            return setmetatable({ ['$type'] = 'variable_reference', name = path }, referenceMetatable)
        end

        modules = {
            copy = 'copy', template = 'template', script = 'script',
            winget = 'winget', scoop = 'scoop', apt = 'apt'
        }

        function task(...)
            local count = select('#', ...)
            local name, module, parameters
            if count == 2 then
                module, parameters = ...
            elseif count == 3 then
                name, module, parameters = ...
                if type(name) ~= 'string' or not name:match('%S') then
                    error('task(name, module, parameters) requires a nonempty name')
                end
            else
                error('task(...) requires a module and parameters, with an optional name')
            end
            if type(module) ~= 'string' or not module:match('%S') then
                error('task(...) requires a nonempty module name')
            end
            if type(parameters) ~= 'table' then
                error('task(...) requires a parameters table')
            end
            local entry = { [module] = parameters }
            if name then entry.name = name end
            tasks[#tasks + 1] = entry
        end

        function print(...)
            local parts = {}
            for index = 1, select('#', ...) do
                parts[index] = tostring(select(index, ...))
            end
            writeDiagnostic(table.concat(parts, '\t'))
        end

        local clock = os.clock
        local fail = error
        local started = clock()
        local instructions = 0
        debug.sethook(function()
            instructions = instructions + 1000
            if instructions > 100000 or clock() - started > 2 then
                fail('Lua configuration evaluation limit exceeded')
            end
        end, '', 1000)

        return tasks, function(value) return getmetatable(value) == 'station.array' end
        """;

    public static string BuildVariables(VariableSet variables) =>
        $"vars = {BuildValue(string.Empty, variables.Root)}";

    private static string BuildValue(string path, ConfigValue value)
    {
        if (value is not ConfigObject mapping)
            return $"ref({Quote(path)})";

        var entries = mapping.Select(pair =>
        {
            var childPath = string.IsNullOrEmpty(path) ? pair.Key : $"{path}.{pair.Key}";
            return $"[{Quote(pair.Key)}] = {BuildValue(childPath, pair.Value)}";
        });
        return $"{{ {string.Join(", ", entries)} }}";
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in value)
        {
            if (character is '\\' or '"')
                builder.Append('\\').Append(character);
            else if (char.IsControl(character))
                builder.Append("\\u{").Append(((int)character).ToString("X")).Append('}');
            else
                builder.Append(character);
        }
        return builder.Append('"').ToString();
    }
}
