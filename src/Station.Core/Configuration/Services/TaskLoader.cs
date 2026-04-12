using System.Globalization;

using Station.Abstractions.Interfaces;
using Station.Abstractions.Models;
using Station.Core.Configuration.Models;
using Station.Core.Infrastructure.Configuration;

namespace Station.Core.Configuration.Services;

internal sealed class TaskLoader
{
    private readonly IConfigArtifactResolver artifactResolver;
    private readonly IReadOnlyDictionary<string, IRoleDocumentLoader> roleLoaders;
    private readonly ITemplateEngine templateEngine;

    public TaskLoader(
        ITemplateEngine templateEngine,
        IConfigArtifactResolver artifactResolver,
        IEnumerable<IRoleDocumentLoader> roleLoaders)
    {
        this.templateEngine = templateEngine;
        this.artifactResolver = artifactResolver;
        this.roleLoaders = roleLoaders.ToDictionary(
            static loader => loader.Id,
            static loader => loader,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<LoadedTask> LoadRootTasks(
        string configRoot,
        IDictionary<string, object?> manifest,
        VariableSet variables,
        string manifestPath)
    {
        var taskValues = manifest.GetOptionalList("tasks") ?? [];
        return ParseTasks(
            new ConfigArray(taskValues.Select(static value =>
                ConfigValueFactory.FromClrValue(value))),
            roleName: null,
            taskRoot: configRoot,
            roleRoot: null,
            variables,
            manifestPath,
            "station.tasks");
    }

    public IReadOnlyList<LoadedTask> LoadRoleTasks(ResolvedRole role, VariableSet variables)
    {
        var artifact = artifactResolver.ResolveRoleTaskArtifact(role.RoleDirectory, role.FullName);
        var loader = roleLoaders.TryGetValue(artifact.LoaderId, out var resolvedLoader)
            ? resolvedLoader
            : throw new ConfigurationException(
                $"No role loader is registered for '{artifact.DisplayName}'.");

        var tasks = loader.LoadRoleTasks(
            artifact,
            new ConfigEvaluationContext(
                role.RoleRoot,
                artifact,
                variables,
                role.ShortName,
                role.RoleRoot));

        return ParseTasks(
            tasks,
            role.ShortName,
            role.RoleRoot,
            role.RoleRoot,
            variables,
            artifact.FullPath,
            $"{role.ShortName}.tasks");
    }

    private List<LoadedTask> ParseTasks(
        ConfigArray taskNodes,
        string? roleName,
        string taskRoot,
        string? roleRoot,
        VariableSet variables,
        string sourceName,
        string defaultNamePrefix)
    {
        var tasks = new List<LoadedTask>();
        for (var index = 0; index < taskNodes.Count; index++)
        {
            if (taskNodes[index] is not ConfigObject taskMapping)
            {
                throw new ConfigurationException(
                    $"Task #{index + 1} in '{sourceName}' must be a mapping.");
            }

            var renderedTask = ParseTask(taskMapping, variables, sourceName, defaultNamePrefix, index + 1);
            tasks.Add(new LoadedTask(
                renderedTask.Name,
                renderedTask.ModuleName,
                renderedTask.Parameters,
                taskRoot,
                roleName,
                roleRoot,
                variables));
        }

        return tasks;
    }

    private (string Name, string ModuleName, ConfigObject Parameters) ParseTask(
        ConfigObject taskMapping,
        VariableSet variables,
        string sourceName,
        string defaultNamePrefix,
        int oneBasedIndex)
    {
        ConfigValue? explicitName = null;
        KeyValuePair<string, ConfigValue>? moduleEntry = null;

        foreach (var (key, value) in taskMapping)
        {
            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
            {
                explicitName = value;
                continue;
            }

            if (moduleEntry is not null)
            {
                throw new ConfigurationException(
                    $"Task in '{sourceName}' must declare exactly one module key besides 'name'.");
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
                $"Task in '{sourceName}' declares an empty module name.");
        }

        if (moduleEntry.Value.Value is not ConfigObject rawParameters)
        {
            throw new ConfigurationException(
                $"Task module '{moduleName}' in '{sourceName}' must map to an object for now.");
        }

        var taskName = explicitName is null
            ? templateEngine.Render($"{defaultNamePrefix}.{oneBasedIndex}", variables, sourceName)
            : RenderTaskName(explicitName, variables, sourceName);

        return (taskName, moduleName, rawParameters);
    }

    private string RenderTaskName(ConfigValue nameValue, VariableSet variables, string sourceName)
    {
        var resolvedName = ConfigValueUtilities.ResolveValue(nameValue, variables, templateEngine, sourceName);
        return resolvedName switch
        {
            ConfigScalar { Kind: ConfigScalarKind.String, Value: string stringValue } => stringValue,
            ConfigScalar scalar => Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)
                ?? throw new ConfigurationException(
                    $"Task name in '{sourceName}' must resolve to a non-empty scalar."),
            _ => throw new ConfigurationException(
                $"Task name in '{sourceName}' must resolve to a scalar.")
        };
    }
}
