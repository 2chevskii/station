using Station.Abstractions.Models;
using Station.Core.Configuration.Models;

namespace Station.Core.Configuration.Services;

internal interface IRoleDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    ConfigArray LoadRoleTasks(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
