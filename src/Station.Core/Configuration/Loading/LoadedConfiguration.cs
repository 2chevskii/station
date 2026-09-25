using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Sources;

namespace Station.Core.Configuration.Loading;

public sealed record LoadedConfiguration(
    string ConfigRoot,
    VariableSet RootVariables,
    IReadOnlyList<PluginDeclaration> PluginDeclarations,
    IReadOnlyList<string> RequestedRoles,
    IReadOnlyList<LoadedTask> RootTasks
);
