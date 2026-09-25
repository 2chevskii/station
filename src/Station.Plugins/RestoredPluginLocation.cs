namespace Station.Plugins.Providers;

public sealed record RestoredPluginLocation(string PluginRoot, bool CacheHit, bool IsLocal);
