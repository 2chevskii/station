namespace Station.Core.Plugins.Providers;

internal sealed record RestoredPluginLocation(string PluginRoot, bool CacheHit, bool IsLocal);
