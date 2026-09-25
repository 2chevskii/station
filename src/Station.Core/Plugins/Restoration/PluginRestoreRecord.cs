namespace Station.Core.Plugins.Restoration;

public sealed record PluginRestoreRecord(
    string Requirement,
    string? PluginName,
    string? Version,
    string PluginRoot,
    bool CacheHit,
    bool IsLocal
);