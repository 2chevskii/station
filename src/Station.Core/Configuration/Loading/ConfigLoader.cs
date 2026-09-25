using Station.Core.Configuration.Artifacts;
using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;
using Station.Plugins.Abstractions.Requirements;

namespace Station.Core.Configuration.Loading;

public sealed class ConfigLoader
{
    private readonly ConfigTreeDiscovery configTreeDiscovery;
    private readonly LocalConfigurationDiscovery localDiscovery;
    private readonly PluginRequirementParser pluginRequirementParser;
    private readonly PluginManifestLoader pluginManifestLoader;
    private readonly TaskLoader taskLoader;
    private readonly VariableScopeLoader variableScopeLoader;
    private readonly YamlConfigurationReader yamlReader;

    public ConfigLoader()
        : this(new ScribanTemplateRenderer()) { }

    internal ConfigLoader(ITemplateEngine templateEngine)
    {
        configTreeDiscovery = new ConfigTreeDiscovery();
        yamlReader = new YamlConfigurationReader();
        var javaScriptRuntime = new JavaScriptConfigRuntime();
        IReadOnlyList<IRoleDocumentLoader> roleLoaders1 = [
            new YamlRoleDocumentLoader(yamlReader),
            new JavaScriptRoleDocumentLoader(javaScriptRuntime),
        ];
        IReadOnlyList<IVariableDocumentLoader> variableLoaders1 = [
            new YamlVariableDocumentLoader(yamlReader),
            new JavaScriptVariableDocumentLoader(javaScriptRuntime),
        ];
        IConfigArtifactResolver artifactResolver1 = new ConfigArtifactResolver(roleLoaders1, variableLoaders1);
        variableScopeLoader = new VariableScopeLoader(artifactResolver1, variableLoaders1);
        taskLoader = new TaskLoader(templateEngine, artifactResolver1, roleLoaders1);
        pluginRequirementParser = new PluginRequirementParser();
        pluginManifestLoader = new PluginManifestLoader(
            yamlReader,
            (ConfigArtifactResolver)artifactResolver1,
            configTreeDiscovery
        );
        localDiscovery = new LocalConfigurationDiscovery();
    }

    public LoadedConfiguration Load(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null
    )
    {
        var (tree, manifest, manifestPath) = ReadRootManifest(configRoot);
        var variables = variableScopeLoader.LoadRootVariables(
            tree.RootPath,
            tree,
            manifest,
            manifestPath,
            cliVariables
        );

        return new LoadedConfiguration(
            tree.RootPath,
            variables,
            pluginRequirementParser.Parse(manifest.GetOptionalList("plugins"), manifestPath),
            manifest.GetRequiredStringList("roles", manifestPath),
            taskLoader.LoadRootTasks(tree.RootPath, manifest, variables, manifestPath)
        );
    }

    public (
        string ConfigRoot,
        IReadOnlyList<PluginRequirement> Requirements,
        VariableSet Variables
    ) LoadPluginRequirements(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null
    )
    {
        var (tree, manifest, manifestPath) = ReadRootManifest(configRoot);
        return (
            tree.RootPath,
            pluginRequirementParser.Parse(manifest.GetOptionalList("plugins"), manifestPath),
            variableScopeLoader.LoadRootVariables(
                tree.RootPath,
                tree,
                manifest,
                manifestPath,
                cliVariables
            )
        );
    }

    private (
        DiscoveredConfigTree Tree,
        IDictionary<string, object?> Manifest,
        string ManifestPath
    ) ReadRootManifest(string configRoot)
    {
        var tree = configTreeDiscovery.Discover(configRoot);
        if (tree.RootManifest is null)
        {
            throw new ConfigurationException(
                $"Missing root manifest '{configRoot.AsLocalPath().ResolveToCurrentDirectory() / "station.yaml"}'."
            );
        }

        var manifestPath = tree.RootManifest.FullPath;
        var rootNode = yamlReader.LoadObject(manifestPath);
        if (rootNode is not IDictionary<string, object?> manifest)
        {
            throw new ConfigurationException(
                $"Root manifest '{manifestPath}' must contain a top-level mapping."
            );
        }

        return (tree, manifest, manifestPath);
    }

    public IReadOnlyList<LoadedTask> LoadRoleTasks(ResolvedRole role, VariableSet rootVariables)
    {
        var properties = taskLoader.ReadRoleProperties(role);
        return taskLoader.LoadRoleTasks(
            role,
            variableScopeLoader.CreateRoleVariables(role, rootVariables, properties.Defaults)
        );
    }

    public string? ReadRoleDescription(ResolvedRole role) =>
        taskLoader.ReadRoleProperties(role).Description;

    public ResolvedPluginManifest LoadPluginManifest(string pluginRoot) =>
        pluginManifestLoader.Load(pluginRoot);

    public IReadOnlyList<ResolvedRole> DiscoverLocalRoles(string configRoot) =>
        localDiscovery.DiscoverLocalRoles(configRoot);

    public IReadOnlyList<string> DiscoverLocalModulePaths(string configRoot) =>
        localDiscovery.DiscoverLocalModulePaths(configRoot);

    public string GetPluginsDirectory(string configRoot) =>
        localDiscovery.GetPluginsDirectory(configRoot);
}

internal static class ConfigLoaderValueHelpers
{
    public static IReadOnlyList<string> GetRequiredStringList(
        this IDictionary<string, object?> mapping,
        string propertyName,
        string sourceName
    )
    {
        var values = mapping.GetOptionalList(propertyName);
        if (values is null || values.Count == 0)
        {
            throw new ConfigurationException(
                $"'{sourceName}' must declare a non-empty '{propertyName}' list."
            );
        }

        return values
            .Select(value => value?.ToString()?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    public static IReadOnlyList<object?>? GetOptionalList(
        this IDictionary<string, object?> mapping,
        string propertyName
    )
    {
        if (!mapping.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value as IReadOnlyList<object?>
            ?? (value as IEnumerable<object?>)?.ToArray()
            ?? throw new ConfigurationException($"Property '{propertyName}' must be a list.");
    }

    public static IDictionary<string, object?>? GetOptionalMapping(
        this IDictionary<string, object?> mapping,
        string propertyName
    )
    {
        if (!mapping.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value as IDictionary<string, object?>
            ?? throw new ConfigurationException($"Property '{propertyName}' must be a mapping.");
    }

    public static string RequireString(
        this IDictionary<string, object?> mapping,
        string propertyName,
        string sourceName
    )
    {
        if (
            !mapping.TryGetValue(propertyName, out var value)
            || string.IsNullOrWhiteSpace(value?.ToString())
        )
        {
            throw new ConfigurationException(
                $"Property '{propertyName}' is required in '{sourceName}'."
            );
        }

        return value.ToString()!;
    }

    public static string? GetOptionalString(
        this IDictionary<string, object?> mapping,
        string propertyName
    )
    {
        return
            mapping.TryGetValue(propertyName, out var value)
            && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;
    }
}
