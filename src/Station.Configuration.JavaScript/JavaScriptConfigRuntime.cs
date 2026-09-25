using System.Text;
using System.Text.Json;
using Jint;
using Station.Abstractions.Models;
using Station.Configuration.Abstractions;

namespace Station.Configuration.JavaScript;

public sealed class JavaScriptConfigRuntime
{
    private static readonly string[] BuiltInModules =
    [
        "copy",
        "template",
        "script",
        "winget",
        "scoop",
        "apt",
    ];

    public ConfigObject LoadVariables(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var engine = CreateEngine(artifact, context);
        var module = engine.Modules.Import(GetImportSpecifier(artifact));
        var exportValue = module.Get("default").ToObject();
        return ConfigValueFactory.RequireObject(
            ConfigValueFactory.FromClrValue(exportValue),
            artifact.DisplayName
        );
    }

    public ConfigArray LoadRoleTasks(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var taskRegistrations = new List<object?>();
        var engine = CreateEngine(artifact, context);
        engine.SetValue(
            "task",
            new Func<object?, object?, object?, object?>(
                (first, second, third) =>
                {
                    RegisterTask(taskRegistrations, first, second, third, artifact);
                    return null;
                }
            )
        );

        engine.Modules.Import(GetImportSpecifier(artifact));
        return new ConfigArray(
            taskRegistrations.Select(static item => ConfigValueFactory.FromClrValue(item))
        );
    }

    private static Engine CreateEngine(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var moduleRoot =
            Path.GetDirectoryName(artifact.FullPath)
            ?? throw new ConfigurationException(
                $"Could not determine the directory for '{artifact.FullPath}'."
            );
        var engine = new Engine(options =>
            options
                .EnableModules(moduleRoot, restrictToBasePath: false)
                .LimitMemory(4_000_000)
                .MaxStatements(10_000)
                .TimeoutInterval(TimeSpan.FromSeconds(2))
        );

        engine.Modules.Add(
            "station:vars",
            builder => builder.AddSource(BuildVarsModuleSource(context.VisibleVariables))
        );
        engine.Modules.Add(
            "station:modules/builtin",
            builder => builder.AddSource(BuildBuiltinModuleSource())
        );
        return engine;
    }

    private static string GetImportSpecifier(ResolvedConfigArtifact artifact) =>
        $"./{Path.GetFileName(artifact.FullPath).Replace('\\', '/')}";

    private static string BuildBuiltinModuleSource()
    {
        var builder = new StringBuilder();
        foreach (var module in BuiltInModules)
        {
            builder
                .Append("export const ")
                .Append(module)
                .Append(" = ")
                .Append(JsonSerializer.Serialize(module))
                .AppendLine(";");
        }

        return builder.ToString();
    }

    private static string BuildVarsModuleSource(VariableSet variables)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            """
            class StationVariableReference {
              constructor(name) {
                this.$type = 'variable_reference';
                this.name = name;
              }

              toString() {
                return `{{ ${this.name} }}`;
              }

              valueOf() {
                return this.toString();
              }

              [Symbol.toPrimitive]() {
                return this.toString();
              }
            }
            """
        );
        builder.AppendLine("const __ref = path => new StationVariableReference(path);");
        builder.AppendLine("const __default = {};");

        foreach (var (key, value) in variables.Root)
        {
            builder
                .Append("__default[")
                .Append(JsonSerializer.Serialize(key))
                .Append("] = ")
                .Append(BuildVarsValueExpression(key, value))
                .AppendLine(";");

            if (IsValidIdentifier(key))
            {
                builder
                    .Append("export const ")
                    .Append(key)
                    .Append(" = __ref(")
                    .Append(JsonSerializer.Serialize(key))
                    .AppendLine(");");
            }
        }

        builder.AppendLine("export default __default;");
        return builder.ToString();
    }

    private static string BuildVarsValueExpression(string path, ConfigValue value)
    {
        if (value is ConfigObject configObject)
        {
            var children = configObject.Select(pair =>
                $"{JsonSerializer.Serialize(pair.Key)}: {BuildVarsValueExpression($"{path}.{pair.Key}", pair.Value)}"
            );
            return $"{{ {string.Join(", ", children)} }}";
        }

        return $"__ref({JsonSerializer.Serialize(path)})";
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!(char.IsLetter(value[0]) || value[0] == '_' || value[0] == '$'))
        {
            return false;
        }

        return value
            .Skip(1)
            .All(static character => char.IsLetterOrDigit(character) || character is '_' or '$');
    }

    private static void RegisterTask(
        ICollection<object?> tasks,
        object? first,
        object? second,
        object? third,
        ResolvedConfigArtifact artifact
    )
    {
        string? explicitName = null;
        object? moduleReference;
        object? parametersObject;

        if (third is null)
        {
            moduleReference = first;
            parametersObject = second;
        }
        else
        {
            explicitName = first?.ToString();
            moduleReference = second;
            parametersObject = third;
        }

        var moduleName = moduleReference?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new ConfigurationException(
                $"JavaScript role file '{artifact.FullPath}' called task(...) with an invalid module reference."
            );
        }

        if (parametersObject is not IDictionary<string, object?> parameters)
        {
            throw new ConfigurationException(
                $"JavaScript role file '{artifact.FullPath}' called task(...) with a non-object parameters value."
            );
        }

        var task = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            task["name"] = explicitName;
        }

        task[moduleName] = parameters;
        tasks.Add(task);
    }
}
