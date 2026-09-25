using Station.Abstractions.Configuration;

namespace Station.Configuration.JavaScript;

public sealed class JavaScriptVariableDocumentLoader : IVariableDocumentLoader
{
    private readonly JavaScriptConfigRuntime runtime;

    public JavaScriptVariableDocumentLoader(JavaScriptConfigRuntime runtime)
    {
        this.runtime = runtime;
    }

    public string Id => "javascript";

    public bool Supports(ConfigArtifactKind kind, string fileName) =>
        kind switch
        {
            ConfigArtifactKind.RootVarsEntry => fileName.EndsWith(
                ".js",
                StringComparison.OrdinalIgnoreCase
            ),
            ConfigArtifactKind.RoleDefaults => string.Equals(
                fileName,
                "defaults.js",
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false,
        };

    public ConfigObject LoadVariables(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    ) => runtime.LoadVariables(artifact, context);
}
