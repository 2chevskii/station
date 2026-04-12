using System.Text.Json.Nodes;
using Station.Abstractions.Models;
using Station.Core.Infrastructure.Json;

namespace Station.Core.Configuration.Services;

internal sealed class VariableScopeLoader
{
    private const string DefaultsFileName = "defaults.yaml";
    private const string EnvironmentPrefix = "CONFIG_TOOL_VAR_";
    private const string VarsDirectoryName = "vars";

    private readonly YamlConfigurationReader yamlReader;

    public VariableScopeLoader(YamlConfigurationReader yamlReader)
    {
        this.yamlReader = yamlReader;
    }

    public VariableSet LoadRootVariables(
        string configRoot,
        IDictionary<string, object?> manifest,
        string manifestPath,
        IReadOnlyDictionary<string, string>? cliVariables)
    {
        var rootVariables = new JsonObject();
        MergeVariables(rootVariables, LoadVarsDirectory(configRoot));
        MergeVariables(
            rootVariables,
            ToVariableObject(manifest.GetOptionalMapping("vars"), manifestPath, "vars")
        );
        MergeVariables(rootVariables, LoadEnvironmentVariables());
        MergeVariables(rootVariables, LoadCliVariables(cliVariables));
        return new VariableSet(rootVariables);
    }

    public VariableSet CreateRoleVariables(string roleRoot, VariableSet rootVariables)
    {
        var roleVariables = LoadOptionalDefaults(roleRoot);
        MergeVariables(roleVariables, rootVariables.Root);
        return new VariableSet(roleVariables);
    }

    private JsonObject LoadVarsDirectory(string configRoot)
    {
        var varsRoot = Path.Combine(configRoot, VarsDirectoryName);
        if (!Directory.Exists(varsRoot))
        {
            return new JsonObject();
        }

        var variables = new JsonObject();
        foreach (
            var file in Directory
                .GetFiles(varsRoot, "*.yaml", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
        )
        {
            MergeVariables(variables, ToVariableObject(yamlReader.LoadObject(file), file));
        }

        return variables;
    }

    private JsonObject LoadOptionalDefaults(string roleRoot)
    {
        var defaultsPath = Path.Combine(roleRoot, DefaultsFileName);
        return File.Exists(defaultsPath)
            ? ToVariableObject(yamlReader.LoadObject(defaultsPath), defaultsPath)
            : new JsonObject();
    }

    private static JsonObject LoadEnvironmentVariables()
    {
        var variables = new JsonObject();
        foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<object>())
        {
            var keyString = key.ToString();
            if (
                keyString is null
                || !keyString.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            SetVariablePath(
                variables,
                keyString[EnvironmentPrefix.Length..],
                Environment.GetEnvironmentVariable(keyString) ?? string.Empty
            );
        }

        return variables;
    }

    private static JsonObject LoadCliVariables(IReadOnlyDictionary<string, string>? cliVariables)
    {
        var variables = new JsonObject();
        foreach (var (key, value) in cliVariables ?? new Dictionary<string, string>())
        {
            SetVariablePath(variables, key, value);
        }

        return variables;
    }

    private static void MergeVariables(JsonObject target, JsonNode? source)
    {
        if (source is null)
        {
            return;
        }

        if (source is not JsonObject sourceObject)
        {
            throw new ConfigurationException("Variable scopes must be objects.");
        }

        foreach (var (key, value) in sourceObject)
        {
            target[key] = MergeVariableNode(target[key], value);
        }
    }

    private static JsonNode? MergeVariableNode(JsonNode? current, JsonNode? incoming)
    {
        if (current is JsonObject currentObject && incoming is JsonObject incomingObject)
        {
            var merged = (JsonObject)currentObject.DeepClone();
            foreach (var (key, value) in incomingObject)
            {
                merged[key] = MergeVariableNode(merged[key], value);
            }

            return merged;
        }

        return incoming?.DeepClone();
    }

    private static JsonObject ToVariableObject(
        object? value,
        string sourceName,
        string? propertyName = null)
    {
        if (value is null)
        {
            return new JsonObject();
        }

        if (value is not IDictionary<string, object?> variables)
        {
            var name = propertyName is null ? sourceName : $"{sourceName}:{propertyName}";
            throw new ConfigurationException(
                $"Variables from '{name}' must contain a top-level mapping."
            );
        }

        return JsonNodeHelpers.ToJsonObject(variables);
    }

    private static void SetVariablePath(JsonObject variables, string path, string value)
    {
        var segments = path.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (segments.Length == 0)
        {
            throw new ConfigurationException("Variable names must not be empty.");
        }

        JsonObject current = variables;
        foreach (var segment in segments[..^1])
        {
            if (current[segment] is not JsonObject child)
            {
                child = new JsonObject();
                current[segment] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }
}
