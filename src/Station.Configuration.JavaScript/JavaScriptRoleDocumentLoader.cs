using Station.Abstractions.Configuration;

namespace Station.Configuration.JavaScript;

public sealed class JavaScriptRoleDocumentLoader : IRoleDocumentLoader
{
    private readonly JavaScriptConfigRuntime runtime;

    public JavaScriptRoleDocumentLoader( JavaScriptConfigRuntime runtime )
    {
        this.runtime = runtime;
    }

    public string Id => "javascript";

    public bool Supports( ConfigArtifactKind kind, string fileName ) =>
        kind == ConfigArtifactKind.RoleTasks
        && string.Equals( fileName, "role.js", StringComparison.OrdinalIgnoreCase );

    public RoleDocumentProperties ReadProperties( ResolvedConfigArtifact artifact ) =>
        new RoleDocumentProperties( new ConfigObject(), null );

    public ConfigArray LoadRoleTasks(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    ) => runtime.LoadRoleTasks( artifact, context );
}