namespace Station.Core.Configuration.Roles;

internal sealed class RoleMetadataReader(YamlConfigurationReader yamlReader)
{
    private const string MetadataYamlFileName = "metadata.yaml";
    private const string MetadataYmlFileName = "metadata.yml";

    public string? ReadDescription(string roleRoot)
    {
        var yamlPath = Path.Combine(roleRoot, MetadataYamlFileName);
        var ymlPath = Path.Combine(roleRoot, MetadataYmlFileName);
        if (File.Exists(yamlPath) && File.Exists(ymlPath))
        {
            throw new ConfigurationException(
                $"Role '{roleRoot}' contains both '{MetadataYamlFileName}' and '{MetadataYmlFileName}'."
            );
        }

        var metadataPath = File.Exists(yamlPath) ? yamlPath : File.Exists(ymlPath) ? ymlPath : null;
        if (metadataPath is null)
        {
            return null;
        }

        var description = yamlReader.Load<RoleMetadata>(metadataPath).Description;
        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}
