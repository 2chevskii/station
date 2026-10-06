using Station.Abstractions.Configuration;
using Station.Configuration.Abstractions.Documents;

namespace Station.Configuration.Lua;

public sealed class LuaRoleDocumentLoader(LuaConfigRuntime runtime) : IRoleDocumentLoader
{
    public string Id => "lua";

    public bool Supports(ConfigArtifactKind kind, string fileName) =>
        kind == ConfigArtifactKind.RoleTasks
        && string.Equals(fileName, "role.lua", StringComparison.OrdinalIgnoreCase);

    public RoleDocumentProperties ReadProperties(ResolvedConfigArtifact artifact) =>
        new RoleDocumentProperties(new ConfigObject(), null);

    public ConfigArray LoadRoleTasks(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    ) => runtime.LoadRoleTasks(artifact, context);
}
