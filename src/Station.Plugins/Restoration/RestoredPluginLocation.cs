namespace Station.Plugins.Restoration;

public sealed record RestoredPluginLocation(string PluginRoot, bool CacheHit, bool IsLocal);
