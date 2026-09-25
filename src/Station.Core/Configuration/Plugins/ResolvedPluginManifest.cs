using Station.Core.Configuration.Discovery;

namespace Station.Core.Configuration.Plugins;

public sealed record ResolvedPluginManifest(
    string Name,
    string Version,
    string PluginRoot,
    DiscoveredConfigTree Tree,
    IReadOnlyList<string> Roles,
    IReadOnlyList<PluginModuleExport> Modules
);