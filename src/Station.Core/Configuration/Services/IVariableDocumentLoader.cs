using Station.Abstractions.Models;
using Station.Core.Configuration.Models;

namespace Station.Core.Configuration.Services;

internal interface IVariableDocumentLoader
{
    string Id { get; }

    bool Supports(ConfigArtifactKind kind, string fileName);

    ConfigObject LoadVariables(ResolvedConfigArtifact artifact, ConfigEvaluationContext context);
}
