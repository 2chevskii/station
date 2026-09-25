using Station.Abstractions.Configuration;

namespace Station.Configuration.Abstractions.Documents;

public interface IRoleDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    RoleDocumentProperties ReadProperties(ResolvedConfigArtifact artifact);

    ConfigArray LoadRoleTasks(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
