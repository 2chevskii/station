using Station.Core.Configuration.Plugins;

namespace Station.Core.Plugins.Restoration;

public sealed record PluginRestoreResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<PluginRestoreRecord> Plugins,
    IReadOnlyList<ResolvedPluginManifest> RestoredPlugins
);
