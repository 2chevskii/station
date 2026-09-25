namespace Station.Core.Configuration.Models;

public sealed record LoadedConfiguration(
    string ConfigRoot,
    VariableSet RootVariables,
    IReadOnlyList<PluginDeclaration> PluginDeclarations,
    IReadOnlyList<string> RequestedRoles,
    IReadOnlyList<LoadedTask> RootTasks
);
