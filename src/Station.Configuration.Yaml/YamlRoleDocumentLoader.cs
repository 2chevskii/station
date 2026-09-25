using Station.Abstractions.Configuration;

namespace Station.Configuration.Yaml;

public sealed class YamlRoleDocumentLoader : IRoleDocumentLoader
{
    private readonly YamlConfigurationReader yamlReader;

    public YamlRoleDocumentLoader(YamlConfigurationReader yamlReader)
    {
        this.yamlReader = yamlReader;
    }

    public string Id => "yaml";

    public bool Supports(ConfigArtifactKind kind, string fileName) =>
        kind == ConfigArtifactKind.RoleTasks
        && string.Equals(fileName, "role.yaml", StringComparison.OrdinalIgnoreCase);

    public ConfigArray LoadRoleTasks(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var value = ConfigValueFactory.FromClrValue(yamlReader.LoadObject(artifact.FullPath));
        return ConfigValueFactory.RequireArray(value, artifact.DisplayName);
    }
}
