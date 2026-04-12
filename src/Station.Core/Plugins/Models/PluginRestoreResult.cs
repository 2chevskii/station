using Station.Core.Configuration;

namespace Station.Core.Plugins;

public sealed record PluginRestoreResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<PluginRestoreRecord> Plugins,
    IReadOnlyList<ResolvedPluginManifest> RestoredPlugins);
