using Station.Abstractions.Interfaces;
using Station.Core.Configuration.Models;
using Station.Core.Infrastructure.Templates;

namespace Station.Core.Configuration.Services;

public sealed class ConfigLoader
{
    private const string RootManifestFileName = "station.yaml";

    private readonly LocalConfigurationDiscovery localDiscovery;
    private readonly PluginDeclarationParser pluginDeclarationParser;
    private readonly PluginManifestLoader pluginManifestLoader;
    private readonly TaskLoader taskLoader;
    private readonly VariableScopeLoader variableScopeLoader;
    private readonly YamlConfigurationReader yamlReader;

    public ConfigLoader()
        : this(new ScribanTemplateRenderer())
    {
    }

    internal ConfigLoader(ITemplateEngine templateEngine)
    {
        yamlReader = new YamlConfigurationReader();
        variableScopeLoader = new VariableScopeLoader(yamlReader);
        taskLoader = new TaskLoader(templateEngine, yamlReader);
        pluginDeclarationParser = new PluginDeclarationParser();
        pluginManifestLoader = new PluginManifestLoader(yamlReader);
        localDiscovery = new LocalConfigurationDiscovery();
    }

    public LoadedConfiguration Load(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null)
    {
        var fullConfigRoot = Path.GetFullPath(configRoot);
        var manifestPath = Path.Combine(fullConfigRoot, RootManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new ConfigurationException($"Missing root manifest '{manifestPath}'.");
        }

        var rootNode = yamlReader.LoadObject(manifestPath);
        if (rootNode is not IDictionary<string, object?> manifest)
        {
            throw new ConfigurationException(
                $"Root manifest '{manifestPath}' must contain a top-level mapping."
            );
        }

        var variables = variableScopeLoader.LoadRootVariables(
            fullConfigRoot,
            manifest,
            manifestPath,
            cliVariables
        );

        return new LoadedConfiguration(
            fullConfigRoot,
            variables,
            pluginDeclarationParser.Parse(manifest.GetOptionalList("plugins"), manifestPath),
            manifest.GetRequiredStringList("roles", manifestPath),
            taskLoader.LoadRootTasks(fullConfigRoot, manifest, variables, manifestPath)
        );
    }

    public IReadOnlyList<LoadedTask> LoadRoleTasks(
        ResolvedRole role,
        VariableSet rootVariables)
    {
        return taskLoader.LoadRoleTasks(
            role,
            variableScopeLoader.CreateRoleVariables(role.RoleRoot, rootVariables)
        );
    }

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
        string sourceName)
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
        string propertyName)
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
        string propertyName)
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
        string sourceName)
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

        return value!.ToString()!;
    }

    public static string? GetOptionalString(
        this IDictionary<string, object?> mapping,
        string propertyName)
    {
        return
            mapping.TryGetValue(propertyName, out var value)
            && !string.IsNullOrWhiteSpace(value?.ToString())
                ? value!.ToString()
                : null;
    }
}
