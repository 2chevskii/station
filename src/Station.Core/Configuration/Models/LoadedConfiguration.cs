using Station.Abstractions.Models;

namespace Station.Core.Configuration;

public sealed record LoadedConfiguration(
    string ConfigRoot,
    VariableSet RootVariables,
    IReadOnlyList<PluginDeclaration> PluginDeclarations,
    IReadOnlyList<string> RequestedRoles,
    IReadOnlyList<LoadedTask> RootTasks);
