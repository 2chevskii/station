namespace Station.Core.Plugins.Models;

public sealed record PluginRestoreRecord(
    string Declaration,
    string? PluginName,
    string? Version,
    string PluginRoot,
    bool CacheHit,
    bool IsLocal
);
