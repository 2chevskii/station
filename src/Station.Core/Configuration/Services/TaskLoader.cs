using System.Text.Json.Nodes;
using Station.Abstractions.Models;
using Station.Abstractions.Interfaces;
using Station.Core.Configuration.Models;
using Station.Core.Infrastructure.Json;

namespace Station.Core.Configuration.Services;

internal sealed class TaskLoader
{
    private const string RoleFileName = "role.yaml";

    private readonly ITemplateEngine templateEngine;
    private readonly YamlConfigurationReader yamlReader;

    public TaskLoader(ITemplateEngine templateEngine, YamlConfigurationReader yamlReader)
    {
        this.templateEngine = templateEngine;
        this.yamlReader = yamlReader;
    }

    public IReadOnlyList<LoadedTask> LoadRootTasks(
        string configRoot,
        IDictionary<string, object?> manifest,
        VariableSet variables,
        string manifestPath)
    {
        return ParseTasks(
            manifest.GetOptionalList("tasks"),
            roleName: null,
            taskRoot: configRoot,
            roleRoot: null,
            variables,
            manifestPath,
            "station.tasks"
        );
    }

    public IReadOnlyList<LoadedTask> LoadRoleTasks(ResolvedRole role, VariableSet variables)
    {
        var roleFilePath = Path.Combine(role.RoleRoot, RoleFileName);
        if (!File.Exists(roleFilePath))
        {
            throw new ConfigurationException(
                $"Role '{role.FullName}' is missing '{RoleFileName}'."
            );
        }

        var taskNodes = yamlReader.LoadObject(roleFilePath) as IEnumerable<object?>;
        if (taskNodes is null)
        {
            throw new ConfigurationException(
                $"Role file '{roleFilePath}' must contain a top-level task list."
            );
        }

        return ParseTasks(
            taskNodes.ToList(),
            role.ShortName,
            role.RoleRoot,
            role.RoleRoot,
            variables,
            roleFilePath,
            $"{role.ShortName}.tasks"
        );
    }

    private List<LoadedTask> ParseTasks(
        IReadOnlyList<object?>? taskNodes,
        string? roleName,
        string taskRoot,
        string? roleRoot,
        VariableSet variables,
        string sourceName,
        string defaultNamePrefix)
    {
        var tasks = new List<LoadedTask>();
        if (taskNodes is null)
        {
            return tasks;
        }

        for (var index = 0; index < taskNodes.Count; index++)
        {
            if (taskNodes[index] is not IDictionary<string, object?> taskMapping)
            {
                throw new ConfigurationException(
                    $"Task #{index + 1} in '{sourceName}' must be a mapping."
                );
            }

            var renderedTask = ParseTask(
                taskMapping,
                variables,
                sourceName,
                defaultNamePrefix,
                index + 1
            );
            tasks.Add(
                new LoadedTask(
                    renderedTask.Name,
                    renderedTask.ModuleName,
                    renderedTask.Parameters,
                    taskRoot,
                    roleName,
                    roleRoot,
                    variables
                )
            );
        }

        return tasks;
    }

    private (string Name, string ModuleName, JsonObject Parameters) ParseTask(
        IDictionary<string, object?> taskMapping,
        VariableSet variables,
        string sourceName,
        string defaultNamePrefix,
        int oneBasedIndex)
    {
        string? explicitName = null;
        KeyValuePair<string, object?>? moduleEntry = null;

        foreach (var (key, value) in taskMapping)
        {
            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
            {
                explicitName = value?.ToString();
                continue;
            }

            if (moduleEntry is not null)
            {
                throw new ConfigurationException(
                    $"Task in '{sourceName}' must declare exactly one module key besides 'name'."
                );
            }

            moduleEntry = KeyValuePair.Create(key, value);
        }

        if (moduleEntry is null)
        {
            throw new ConfigurationException($"Task in '{sourceName}' does not declare a module.");
        }

        var moduleName = moduleEntry.Value.Key.Trim();
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new ConfigurationException(
                $"Task in '{sourceName}' declares an empty module name."
            );
        }

        if (moduleEntry.Value.Value is not IDictionary<string, object?> rawParameters)
        {
            throw new ConfigurationException(
                $"Task module '{moduleName}' in '{sourceName}' must map to an object for now."
            );
        }

        var taskNameTemplate = string.IsNullOrWhiteSpace(explicitName)
            ? $"{defaultNamePrefix}.{oneBasedIndex}"
            : explicitName;

        return (
            templateEngine.Render(taskNameTemplate, variables, sourceName),
            moduleName,
            JsonNodeHelpers.ToJsonObject(rawParameters)
        );
    }
}
