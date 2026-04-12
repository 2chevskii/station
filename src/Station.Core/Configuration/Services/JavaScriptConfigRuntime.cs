using System.Text;
using System.Text.Json;

using Jint;

using Station.Abstractions.Models;
using Station.Core.Configuration.Models;
using Station.Core.Infrastructure.Configuration;

namespace Station.Core.Configuration.Services;

internal sealed class JavaScriptConfigRuntime
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

    public ConfigObject LoadVariables(ResolvedConfigArtifact artifact, ConfigEvaluationContext context)
    {
        var prepared = PrepareEvaluationDirectory(artifact, context);
        try
        {
            var engine = CreateEngine(prepared.ModuleRoot);
            var module = engine.Modules.Import(prepared.ImportSpecifier);
            var exportValue = module.Get("default").ToObject();
            return ConfigValueFactory.RequireObject(
                ConfigValueFactory.FromClrValue(exportValue),
                artifact.DisplayName);
        }
        finally
        {
            Cleanup(prepared.ModuleRoot);
        }
    }

    public ConfigArray LoadRoleTasks(ResolvedConfigArtifact artifact, ConfigEvaluationContext context)
    {
        var taskRegistrations = new List<object?>();
        var prepared = PrepareEvaluationDirectory(artifact, context);
        try
        {
            var engine = CreateEngine(prepared.ModuleRoot);
            engine.SetValue(
                "task",
                new Func<object?, object?, object?, object?>((first, second, third) =>
                {
                    RegisterTask(taskRegistrations, first, second, third, artifact);
                    return null;
                }));

            engine.Modules.Import(prepared.ImportSpecifier);
            return new ConfigArray(taskRegistrations.Select(static item =>
                ConfigValueFactory.FromClrValue(item)));
        }
        finally
        {
            Cleanup(prepared.ModuleRoot);
        }
    }

    private static Engine CreateEngine(string moduleRoot)
    {
        var engine = new Engine(options => options
            .EnableModules(moduleRoot)
            .LimitMemory(4_000_000)
            .MaxStatements(10_000)
            .TimeoutInterval(TimeSpan.FromSeconds(2)));
        return engine;
    }

    private (string ModuleRoot, string ImportSpecifier) PrepareEvaluationDirectory(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context)
    {
        var sourceRoot = Path.GetDirectoryName(artifact.FullPath)
            ?? throw new ConfigurationException(
                $"Could not determine the directory for '{artifact.FullPath}'.");
        var moduleRoot = Path.Combine(Path.GetTempPath(), "station-js", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(moduleRoot);

        CopyDirectory(sourceRoot, moduleRoot);
        WriteHelpers(moduleRoot, context.VisibleVariables);
        RewriteStationImports(moduleRoot);

        return (moduleRoot, $"./{Path.GetFileName(artifact.FullPath).Replace('\\', '/')}");
    }

    private static string BuildBuiltinModuleSource()
    {
        var builder = new StringBuilder();
        foreach (var module in BuiltInModules)
        {
            builder.Append("export const ")
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
        builder.AppendLine("const __ref = path => ({ __stationVariableReference: path });");
        builder.AppendLine("const __default = {};");

        foreach (var (key, value) in variables.Root)
        {
            builder.Append("__default[")
                .Append(JsonSerializer.Serialize(key))
                .Append("] = ")
                .Append(BuildVarsValueExpression(key, value))
                .AppendLine(";");

            if (IsValidIdentifier(key))
            {
                builder.Append("export const ")
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
                $"{JsonSerializer.Serialize(pair.Key)}: {BuildVarsValueExpression($"{path}.{pair.Key}", pair.Value)}");
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

        return value.Skip(1).All(static character =>
            char.IsLetterOrDigit(character) || character is '_' or '$');
    }

    private static void RegisterTask(
        ICollection<object?> tasks,
        object? first,
        object? second,
        object? third,
        ResolvedConfigArtifact artifact)
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
                $"JavaScript role file '{artifact.FullPath}' called task(...) with an invalid module reference.");
        }

        if (parametersObject is not IDictionary<string, object?> parameters)
        {
            throw new ConfigurationException(
                $"JavaScript role file '{artifact.FullPath}' called task(...) with a non-object parameters value.");
        }

        var task = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            task["name"] = explicitName;
        }

        task[moduleName] = parameters;
        tasks.Add(task);
    }

    private static void WriteHelpers(string moduleRoot, VariableSet visibleVariables)
    {
        File.WriteAllText(Path.Combine(moduleRoot, "__station_builtin_modules.js"), BuildBuiltinModuleSource());
        File.WriteAllText(Path.Combine(moduleRoot, "__station_vars.js"), BuildVarsModuleSource(visibleVariables));
    }

    private static void RewriteStationImports(string moduleRoot)
    {
        foreach (var file in Directory.GetFiles(moduleRoot, "*.js", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(file) ?? moduleRoot;
            var builtinSpecifier = ToRelativeModuleSpecifier(directory, Path.Combine(moduleRoot, "__station_builtin_modules.js"));
            var varsSpecifier = ToRelativeModuleSpecifier(directory, Path.Combine(moduleRoot, "__station_vars.js"));

            var source = File.ReadAllText(file);
            source = source
                .Replace("'station:modules/builtin'", $"'{builtinSpecifier}'", StringComparison.Ordinal)
                .Replace("\"station:modules/builtin\"", $"\"{builtinSpecifier}\"", StringComparison.Ordinal)
                .Replace("'station:vars'", $"'{varsSpecifier}'", StringComparison.Ordinal)
                .Replace("\"station:vars\"", $"\"{varsSpecifier}\"", StringComparison.Ordinal);
            File.WriteAllText(file, source);
        }
    }

    private static string ToRelativeModuleSpecifier(string fromDirectory, string targetPath)
    {
        var relativePath = Path.GetRelativePath(fromDirectory, targetPath)
            .Replace('\\', '/');
        return relativePath.StartsWith("./", StringComparison.Ordinal)
            || relativePath.StartsWith("../", StringComparison.Ordinal)
            ? relativePath
            : $"./{relativePath}";
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void Cleanup(string moduleRoot)
    {
        if (Directory.Exists(moduleRoot))
        {
            Directory.Delete(moduleRoot, recursive: true);
        }
    }
}
