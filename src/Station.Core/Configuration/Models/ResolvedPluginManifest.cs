namespace Station.Core.Configuration.Models;

public sealed record ResolvedPluginManifest(
    string Name,
    string Version,
    string PluginRoot,
    DiscoveredConfigTree Tree,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Modules);
