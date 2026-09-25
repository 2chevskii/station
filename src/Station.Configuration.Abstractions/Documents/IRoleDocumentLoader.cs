using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;

namespace Station.Configuration.Abstractions.Documents;

public interface IRoleDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    ConfigArray LoadRoleTasks(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
