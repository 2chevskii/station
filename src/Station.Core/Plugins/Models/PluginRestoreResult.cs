using Station.Core.Configuration.Models;

namespace Station.Core.Plugins.Models;

public sealed record PluginRestoreResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<PluginRestoreRecord> Plugins,
    IReadOnlyList<ResolvedPluginManifest> RestoredPlugins
);
