using Station.Core.Configuration.Artifacts;
using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Roles;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Requirements;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Configuration.Plugins;

internal sealed class PluginManifestLoader
{
    private readonly ConfigArtifactResolver artifactResolver;
    private readonly ConfigTreeDiscovery treeDiscovery;
    private readonly YamlConfigurationReader yamlReader;
    private readonly PluginModuleExportParser moduleExportParser = new();

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
        var fullPluginRoot = pluginRoot.AsLocalPath().ResolveToCurrentDirectory();
        var tree = treeDiscovery.Discover(fullPluginRoot.Value);
        var yamlPath = fullPluginRoot / PluginLayout.ManifestYamlFileName;
        var ymlPath = fullPluginRoot / PluginLayout.ManifestYmlFileName;

        if (yamlPath.ExistsFile() && ymlPath.ExistsFile())
        {
            throw new ConfigurationException(
                $"Plugin root '{fullPluginRoot}' contains both '{PluginLayout.ManifestYamlFileName}' and '{PluginLayout.ManifestYmlFileName}'."
            );
        }

        var manifestPath =
            yamlPath.ExistsFile() ? yamlPath
            : ymlPath.ExistsFile() ? ymlPath
            : throw new ConfigurationException(
                $"Plugin root '{fullPluginRoot}' is missing '{PluginLayout.ManifestYamlFileName}' or '{PluginLayout.ManifestYmlFileName}'."
            );

        var manifest = yamlReader.Load<PluginManifest>(manifestPath.Value);
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
        var modules = moduleExportParser.Parse(
            manifest.Modules,
            fullPluginRoot.Value,
            manifestPath.Value
        );

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

        return new ResolvedPluginManifest(
            name,
            version,
            fullPluginRoot.Value,
            tree,
            roles,
            modules
        );
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
            if (export.Contains('.') || export.AsLocalPath().FileName != export)
            {
                throw new ConfigurationException(
                    $"Plugin manifest '{sourceName}' contains invalid {propertyName} export '{export}'. Exports must be short names."
                );
            }
        }

        return exports;
    }
}
