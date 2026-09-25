using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;

namespace Station.Core.Plugins.Restoration;

public sealed record PluginRestoreResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<PluginRestoreRecord> Plugins,
    IReadOnlyList<ResolvedPluginManifest> RestoredPlugins
);
