using Station.Core.Configuration.Artifacts;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;

namespace Station.Core.Configuration.Loading;

internal sealed class VariableScopeLoader
{
    private const string EnvironmentPrefix = "CONFIG_TOOL_VAR_";

    private readonly IConfigArtifactResolver artifactResolver;
    private readonly IReadOnlyDictionary<string, IVariableDocumentLoader> variableLoaders;

    public VariableScopeLoader(
        IConfigArtifactResolver artifactResolver,
        IEnumerable<IVariableDocumentLoader> variableLoaders
    )
    {
        this.artifactResolver = artifactResolver;
        this.variableLoaders = variableLoaders.ToDictionary(
            static loader => loader.Id,
            static loader => loader,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public VariableSet LoadRootVariables(
        string configRoot,
        DiscoveredConfigTree tree,
        IDictionary<string, object?> manifest,
        string manifestPath,
        IReadOnlyDictionary<string, string>? cliVariables
    )
    {
        var rootVariables = new ConfigObject();

        foreach (var artifact in artifactResolver.EnumerateRootVarArtifacts(tree))
        {
            var loader = ResolveLoader(artifact.LoaderId);
            var loaded = loader.LoadVariables(
                artifact,
                new ConfigEvaluationContext(
                    configRoot,
                    artifact,
                    new VariableSet(rootVariables),
                    null,
                    null
                )
            );
            rootVariables = (ConfigObject)ConfigValueUtilities.Merge(rootVariables, loaded);
        }

        var manifestVariables = ToVariableObject(
            manifest.GetOptionalMapping("vars"),
            manifestPath,
            "vars"
        );
        rootVariables = (ConfigObject)ConfigValueUtilities.Merge(rootVariables, manifestVariables);
        rootVariables = (ConfigObject)
            ConfigValueUtilities.Merge(rootVariables, LoadEnvironmentVariables());
        rootVariables = (ConfigObject)
            ConfigValueUtilities.Merge(rootVariables, LoadCliVariables(cliVariables));
        rootVariables = (ConfigObject)
            ConfigValueUtilities.Merge(rootVariables, LoadGlobalVariables());
        return new VariableSet(rootVariables);
    }

    public VariableSet CreateRoleVariables(ResolvedRole role, VariableSet rootVariables)
    {
        var artifact = artifactResolver.ResolveRoleDefaultsArtifact(
            role.RoleDirectory,
            role.FullName
        );
        var roleDefaults = artifact is null
            ? new ConfigObject()
            : ResolveLoader(artifact.LoaderId)
                .LoadVariables(
                    artifact,
                    new ConfigEvaluationContext(
                        role.RoleRoot,
                        artifact,
                        new VariableSet(new ConfigObject()),
                        role.ShortName,
                        role.RoleRoot
                    )
                );

        return new VariableSet(
            (ConfigObject)ConfigValueUtilities.Merge(roleDefaults, rootVariables.Root)
        );
    }

    private IVariableDocumentLoader ResolveLoader(string loaderId) =>
        variableLoaders.TryGetValue(loaderId, out var loader)
            ? loader
            : throw new ConfigurationException(
                $"No variable loader is registered for '{loaderId}'."
            );

    private static ConfigObject LoadGlobalVariables()
    {
        var globals = new ConfigObject(
            new Dictionary<string, ConfigValue>
            {
                {
                    "user_home",
                    new ConfigScalar(
                        ConfigScalarKind.String,
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    )
                },
            }
        );
        return globals;
    }

    private static ConfigObject LoadEnvironmentVariables()
    {
        var variables = new ConfigObject();
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

            variables = SetVariablePath(
                variables,
                keyString[EnvironmentPrefix.Length..],
                ConfigScalar.String(Environment.GetEnvironmentVariable(keyString) ?? string.Empty)
            );
        }

        return variables;
    }

    private static ConfigObject LoadCliVariables(IReadOnlyDictionary<string, string>? cliVariables)
    {
        var variables = new ConfigObject();
        foreach (var (key, value) in cliVariables ?? new Dictionary<string, string>())
        {
            variables = SetVariablePath(variables, key, ConfigScalar.String(value));
        }

        return variables;
    }

    private static ConfigObject ToVariableObject(
        object? value,
        string sourceName,
        string? propertyName = null
    )
    {
        if (value is null)
        {
            return new ConfigObject();
        }

        if (value is not IDictionary<string, object?> variables)
        {
            var name = propertyName is null ? sourceName : $"{sourceName}:{propertyName}";
            throw new ConfigurationException(
                $"Variables from '{name}' must contain a top-level mapping."
            );
        }

        return ConfigValueFactory.RequireObject(
            ConfigValueFactory.FromClrValue(variables),
            propertyName is null ? sourceName : $"{sourceName}:{propertyName}"
        );
    }

    private static ConfigObject SetVariablePath(
        ConfigObject variables,
        string path,
        ConfigValue value
    )
    {
        var segments = ConfigValueUtilities.SplitPath(path).ToArray();
        if (segments.Length == 0)
        {
            throw new ConfigurationException("Variable names must not be empty.");
        }

        return SetVariablePath(variables, segments, 0, value);
    }

    private static ConfigObject SetVariablePath(
        ConfigObject current,
        IReadOnlyList<string> segments,
        int index,
        ConfigValue value
    )
    {
        var updated = current.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase
        );

        if (index == segments.Count - 1)
        {
            updated[segments[index]] = value;
            return new ConfigObject(updated);
        }

        var segment = segments[index];
        var child =
            updated.TryGetValue(segment, out var existing) && existing is ConfigObject childObject
                ? childObject
                : new ConfigObject();
        updated[segment] = SetVariablePath(child, segments, index + 1, value);
        return new ConfigObject(updated);
    }
}
