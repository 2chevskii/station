using Station.Abstractions.Models;
using Station.Configuration.Abstractions.Models;

namespace Station.Configuration.Abstractions.Services;

public interface IRoleDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    ConfigArray LoadRoleTasks(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
