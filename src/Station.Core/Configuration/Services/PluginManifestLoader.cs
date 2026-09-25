using Station.Core.Configuration.Models;

namespace Station.Core.Configuration.Services;

internal sealed class PluginManifestLoader
{
    private const string ModulesDirectoryName = "modules";
    private const string PluginManifestYamlFileName = "plugin-manifest.yaml";
    private const string PluginManifestYmlFileName = "plugin-manifest.yml";

    private readonly ConfigArtifactResolver artifactResolver;
    private readonly ConfigTreeDiscovery treeDiscovery;
    private readonly YamlConfigurationReader yamlReader;

    public PluginManifestLoader(YamlConfigurationReader yamlReader)
        : this(
            yamlReader,
            new ConfigArtifactResolver(
                [
                    new YamlRoleDocumentLoader(yamlReader),
                    new JavaScriptRoleDocumentLoader(new JavaScriptConfigRuntime()),
                ],
                [
                    new YamlVariableDocumentLoader(yamlReader),
                    new JavaScriptVariableDocumentLoader(new JavaScriptConfigRuntime()),
                ]
            ),
            new ConfigTreeDiscovery()
        ) { }

    public PluginManifestLoader(
        YamlConfigurationReader yamlReader,
        ConfigArtifactResolver artifactResolver,
        ConfigTreeDiscovery treeDiscovery
    )
    {
        this.yamlReader = yamlReader;
        this.artifactResolver = artifactResolver;
        this.treeDiscovery = treeDiscovery;
    }

    public ResolvedPluginManifest Load(string pluginRoot)
    {
        var fullPluginRoot = Path.GetFullPath(pluginRoot);
        var tree = treeDiscovery.Discover(fullPluginRoot);
        var yamlPath = Path.Combine(fullPluginRoot, PluginManifestYamlFileName);
        var ymlPath = Path.Combine(fullPluginRoot, PluginManifestYmlFileName);

        if (File.Exists(yamlPath) && File.Exists(ymlPath))
        {
            throw new ConfigurationException(
                $"Plugin root '{fullPluginRoot}' contains both '{PluginManifestYamlFileName}' and '{PluginManifestYmlFileName}'."
            );
        }

        var manifestPath =
            File.Exists(yamlPath) ? yamlPath
            : File.Exists(ymlPath) ? ymlPath
            : throw new ConfigurationException(
                $"Plugin root '{fullPluginRoot}' is missing '{PluginManifestYamlFileName}' or '{PluginManifestYmlFileName}'."
            );

        var manifest = yamlReader.Load<PluginManifestFile>(manifestPath);
        var name = manifest.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' must declare a non-empty 'name'."
            );
        }

        var version = manifest.Version?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' must declare a non-empty 'version'."
            );
        }

        var roles = NormalizeExportList(manifest.Roles, "roles", manifestPath);
        var modules = NormalizeExportList(manifest.Modules, "modules", manifestPath);

        foreach (var role in roles)
        {
            var roleDirectory = tree.RolesRoot?.Directories.FirstOrDefault(directory =>
                string.Equals(directory.Name, role, StringComparison.OrdinalIgnoreCase)
            );
            if (roleDirectory is null)
            {
                throw new ConfigurationException(
                    $"Plugin '{name}' exports role '{role}', but its role directory does not exist."
                );
            }

            _ = artifactResolver.ResolveRoleTaskArtifact(roleDirectory, $"{name}.{role}");
        }

        foreach (var module in modules)
        {
            var modulePath = Path.Combine(
                fullPluginRoot,
                ModulesDirectoryName,
                module,
                $"{module}.dll"
            );
            if (!File.Exists(modulePath))
            {
                throw new ConfigurationException(
                    $"Plugin '{name}' exports module '{module}', but '{modulePath}' does not exist."
                );
            }
        }

        return new ResolvedPluginManifest(name, version, fullPluginRoot, tree, roles, modules);
    }

    private static IReadOnlyList<string> NormalizeExportList(
        IEnumerable<string>? values,
        string propertyName,
        string sourceName
    )
    {
        var exports = (values ?? [])
            .Select(value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (exports.Length == 0)
        {
            return [];
        }

        foreach (var export in exports)
        {
            if (
                export.Contains('.')
                || export.Contains(Path.DirectorySeparatorChar)
                || export.Contains(Path.AltDirectorySeparatorChar)
            )
            {
                throw new ConfigurationException(
                    $"Plugin manifest '{sourceName}' contains invalid {propertyName} export '{export}'. Exports must be short names."
                );
            }
        }

        return exports;
    }
}
