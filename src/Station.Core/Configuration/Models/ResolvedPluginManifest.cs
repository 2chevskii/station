namespace Station.Core.Configuration.Models;

public sealed record ResolvedPluginManifest(
    string Name,
    string Version,
    string PluginRoot,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Modules);
