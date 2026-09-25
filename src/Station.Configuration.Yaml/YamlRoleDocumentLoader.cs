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

    public RoleDocumentProperties ReadProperties(ResolvedConfigArtifact artifact) =>
        ReadDocument(artifact).Properties;

    public ConfigArray LoadRoleTasks(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        return ReadDocument(artifact).Tasks;
    }

    private (ConfigArray Tasks, RoleDocumentProperties Properties) ReadDocument(
        ResolvedConfigArtifact artifact
    )
    {
        var value = ConfigValueFactory.FromClrValue(yamlReader.LoadObject(artifact.FullPath));
        if (value is ConfigArray tasks)
        {
            return (tasks, new RoleDocumentProperties(new ConfigObject(), null));
        }

        if (value is not ConfigObject role)
        {
            throw new ConfigurationException(
                $"Role file '{artifact.DisplayName}' must contain a task list or a mapping with 'tasks'."
            );
        }

        if (!role.TryGetValue("tasks", out var taskValue) || taskValue is not ConfigArray roleTasks)
        {
            throw new ConfigurationException(
                $"Role file '{artifact.DisplayName}' must contain a 'tasks' list."
            );
        }

        var defaults = new ConfigObject();
        if (role.TryGetValue("defaults", out var defaultValue))
        {
            defaults = defaultValue as ConfigObject
                ?? throw new ConfigurationException(
                    $"Property 'defaults' in '{artifact.DisplayName}' must be a mapping."
                );
        }

        string? description = null;
        if (role.TryGetValue("description", out var descriptionValue))
        {
            description = descriptionValue switch
            {
                ConfigNull => null,
                ConfigScalar { Kind: ConfigScalarKind.String, Value: string text } => text.Trim(),
                _ => throw new ConfigurationException(
                    $"Property 'description' in '{artifact.DisplayName}' must be a string."
                ),
            };
        }

        return (
            roleTasks,
            new RoleDocumentProperties(
                defaults,
                string.IsNullOrWhiteSpace(description) ? null : description
            )
        );
    }
}
