using Station.Abstractions.Configuration;

namespace Station.Configuration.Abstractions.Documents;

public interface IRoleDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    ConfigArray LoadRoleTasks(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
