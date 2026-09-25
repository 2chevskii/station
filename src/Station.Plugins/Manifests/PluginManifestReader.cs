using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;
using Station.Configuration.Yaml;
using Station.Plugins.Abstractions.Manifests;

using TruePath.SystemIo;

namespace Station.Plugins.Manifests;

internal sealed class PluginManifestReader
{
    public PluginManifest ReadManifest(string pluginRoot)
    {
        var root = pluginRoot.AsAbsolutePath();
        var yamlPath = root / PluginLayout.ManifestYamlFileName;
        var ymlPath = root / PluginLayout.ManifestYmlFileName;
        if (yamlPath.ExistsFile() && ymlPath.ExistsFile())
        {
            throw new ConfigurationException($"Plugin root '{root}' contains both manifest files.");
        }

        var manifestPath =
            yamlPath.ExistsFile() ? yamlPath
            : ymlPath.ExistsFile() ? ymlPath
            : throw new ConfigurationException($"Plugin root '{root}' has no plugin manifest.");
        var manifest = new YamlConfigurationReader().Load<PluginManifest>(manifestPath.Value);
        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' must declare a name and version."
            );
        }

        return manifest;
    }

    public string ResolvePluginRoot(string candidateRoot)
    {
        if (HasPluginManifest(candidateRoot))
        {
            return candidateRoot;
        }

        var manifests = candidateRoot
            .AsAbsolutePath()
            .GetFiles("*", SearchOption.AllDirectories)
            .Where(path =>
                string.Equals(path.AsAbsolutePath().FileName, PluginLayout.ManifestYamlFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path.AsAbsolutePath().FileName, PluginLayout.ManifestYmlFileName, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        if (manifests.Length == 1)
        {
            return manifests[0].AsAbsolutePath().Parent!.Value.Value;
        }

        throw new ConfigurationException(
            $"Could not locate a unique plugin manifest under '{candidateRoot}'."
        );
    }

    private static bool HasPluginManifest(string root) =>
        (root.AsAbsolutePath() / PluginLayout.ManifestYamlFileName).ExistsFile()
        || (root.AsAbsolutePath() / PluginLayout.ManifestYmlFileName).ExistsFile();
}
