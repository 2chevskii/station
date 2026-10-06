using Station.Abstractions.Configuration;
using Station.Configuration.Abstractions.Documents;

namespace Station.Configuration.Lua;

public sealed class LuaVariableDocumentLoader(LuaConfigRuntime runtime) : IVariableDocumentLoader
{
    public string Id => "lua";

    public bool Supports(ConfigArtifactKind kind, string fileName) =>
        kind switch
        {
            ConfigArtifactKind.RootVarsEntry => fileName.EndsWith(
                ".lua",
                StringComparison.OrdinalIgnoreCase
            ),
            ConfigArtifactKind.RoleDefaults => string.Equals(
                fileName,
                "defaults.lua",
                StringComparison.OrdinalIgnoreCase
            ),
            var _ => false,
        };

    public ConfigObject LoadVariables(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    ) => runtime.LoadVariables(artifact, context);
}
