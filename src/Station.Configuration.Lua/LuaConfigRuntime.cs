using System.Text;
using NLua;
using NLua.Exceptions;
using Station.Abstractions.Configuration;
using Station.Configuration.Abstractions.Documents;
using Station.Configuration.Abstractions.Values;
using LuaState = NLua.Lua;

namespace Station.Configuration.Lua;

public sealed class LuaConfigRuntime
{
    public ConfigObject LoadVariables(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    ) =>
        Evaluate(
            artifact,
            context,
            (converter, _, results) =>
            {
                if (results.Length != 1)
                    throw new ConfigurationException(
                        "Lua variable files must return exactly one table."
                    );

                return ConfigValueFactory.RequireObject(
                    converter.ConvertValue(results[0]),
                    artifact.DisplayName
                );
            }
        );

    public ConfigArray LoadRoleTasks(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    ) =>
        Evaluate(
            artifact,
            context,
            (converter, tasks, results) =>
            {
                if (results.Length != 0)
                    throw new ConfigurationException(
                        "Lua role files must register tasks with task(...), without returning values."
                    );

                return converter.ConvertArray(tasks);
            }
        );

    private static T Evaluate<T>(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context,
        Func<LuaValueConverter, LuaTable, object[], T> convert
    )
    {
        try
        {
            using var lua = new LuaState();
            lua.State.Encoding = Encoding.UTF8;
            lua["__station_print"] = new Action<string>(Console.Error.WriteLine);
            var helpers = lua.DoString(LuaRuntimeSource.Bootstrap, "station:lua");
            using var tasks = (LuaTable)helpers[0];
            using var isArray = (LuaFunction)helpers[1];
            lua.DoString(LuaRuntimeSource.BuildVariables(context.VisibleVariables), "station:vars");

            var artifactDirectory = Path.GetDirectoryName(artifact.FullPath)!;
            lua["package.path"] =
                Path.Combine(artifactDirectory, "?.lua").Replace('\\', '/')
                + ";"
                + lua["package.path"];

            var results = lua.DoString(
                File.ReadAllText(artifact.FullPath),
                $"@{artifact.FullPath}"
            );
            return convert(new LuaValueConverter(isArray), tasks, results);
        }
        catch (LuaScriptException exception)
        {
            throw new ConfigurationException(
                $"Could not evaluate Lua configuration '{artifact.DisplayName}': {exception.Message}"
            );
        }
        catch (ConfigurationException exception)
        {
            throw new ConfigurationException(
                $"Invalid Lua configuration '{artifact.DisplayName}': {exception.Message}"
            );
        }
    }
}
