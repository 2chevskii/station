using Station.Core.Configuration.Models;
using TruePath;
using TruePath.SystemIo;

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
        var fullPluginRoot = new LocalPath(pluginRoot).ResolveToCurrentDirectory();
        var tree = treeDiscovery.Discover(fullPluginRoot.Value);
        var yamlPath = fullPluginRoot / PluginManifestYamlFileName;
        var ymlPath = fullPluginRoot / PluginManifestYmlFileName;

        if (yamlPath.ExistsFile() && ymlPath.ExistsFile())
        {
            throw new ConfigurationException(
                $"Plugin root '{fullPluginRoot}' contains both '{PluginManifestYamlFileName}' and '{PluginManifestYmlFileName}'."
            );
        }

        var manifestPath =
            yamlPath.ExistsFile() ? yamlPath
            : ymlPath.ExistsFile() ? ymlPath
            : throw new ConfigurationException(
                $"Plugin root '{fullPluginRoot}' is missing '{PluginManifestYamlFileName}' or '{PluginManifestYmlFileName}'."
            );

        var manifest = yamlReader.Load<PluginManifestFile>(manifestPath.Value);
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

        var roles = NormalizeExportList(manifest.Roles, "roles", manifestPath.Value);
        var modules = NormalizeExportList(manifest.Modules, "modules", manifestPath.Value);

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
            var modulePath = fullPluginRoot / ModulesDirectoryName / module / $"{module}.dll";
            if (!modulePath.ExistsFile())
            {
                throw new ConfigurationException(
                    $"Plugin '{name}' exports module '{module}', but '{modulePath}' does not exist."
                );
            }
        }

        return new ResolvedPluginManifest(name, version, fullPluginRoot.Value, tree, roles, modules);
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
                || new LocalPath(export).FileName != export
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
