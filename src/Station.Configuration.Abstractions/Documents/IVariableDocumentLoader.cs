using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;

namespace Station.Configuration.Abstractions.Documents;

public interface IVariableDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    ConfigObject LoadVariables(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
