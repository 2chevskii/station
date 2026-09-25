using Station.Plugins.Abstractions.Requirements;

namespace Station.Core.Configuration.Loading;

public sealed record LoadedConfiguration(
    string ConfigRoot,
    VariableSet RootVariables,
    IReadOnlyList<PluginRequirement> PluginRequirements,
    IReadOnlyList<string> RequestedRoles,
    IReadOnlyList<LoadedTask> RootTasks
);