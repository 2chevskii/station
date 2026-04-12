namespace ConfigTool.Core;

public sealed record LoadedConfiguration(
    string ConfigRoot,
    IReadOnlyDictionary<string, string> RootVariables,
    IReadOnlyList<PluginDeclaration> PluginDeclarations,
    IReadOnlyList<string> RequestedRoles,
    IReadOnlyList<LoadedTask> RootTasks);
