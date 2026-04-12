namespace Station.Core.Plugins;

public sealed record PluginRestoreRecord(
    string Declaration,
    string? PluginName,
    string? Version,
    string PluginRoot,
    bool CacheHit,
    bool IsLocal);
