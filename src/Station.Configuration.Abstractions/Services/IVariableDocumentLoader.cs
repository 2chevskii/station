using Station.Abstractions.Models;
using Station.Configuration.Abstractions.Models;

namespace Station.Configuration.Abstractions.Services;

public interface IVariableDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    ConfigObject LoadVariables(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
