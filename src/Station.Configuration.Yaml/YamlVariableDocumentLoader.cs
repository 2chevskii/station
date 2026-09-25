using Station.Abstractions.Models;
using Station.Configuration.Abstractions;

namespace Station.Configuration.Yaml;

public sealed class YamlVariableDocumentLoader : IVariableDocumentLoader
{
    private readonly YamlConfigurationReader yamlReader;

    public YamlVariableDocumentLoader(YamlConfigurationReader yamlReader)
    {
        this.yamlReader = yamlReader;
    }

    public string Id => "yaml";

    public bool Supports(ConfigArtifactKind kind, string fileName) =>
        kind switch
        {
            ConfigArtifactKind.RootVarsEntry => fileName.EndsWith(
                ".yaml",
                StringComparison.OrdinalIgnoreCase
            ),
            ConfigArtifactKind.RoleDefaults => string.Equals(
                fileName,
                "defaults.yaml",
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false,
        };

    public ConfigObject LoadVariables(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var value = ConfigValueFactory.FromClrValue(yamlReader.LoadObject(artifact.FullPath));
        return ConfigValueFactory.RequireObject(value, artifact.DisplayName);
    }
}
