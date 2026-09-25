using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;
using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Providers;

internal sealed class LocalPluginProvider : IPluginProvider
{
    public string Name => "local";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    )
    {
        var pluginsRoot = infrastructure.GetPluginsDirectory(configRoot);
        var candidate = infrastructure.ResolveLocalPluginCandidate(pluginsRoot, requirement.Name!);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ConfigurationException(
                $"Local plugin '{requirement.Name}' was not found under '{pluginsRoot}'."
            );
        }

        if (candidate.AsAbsolutePath().ExistsDirectory())
        {
            return Task.FromResult(
                new PluginSourceSnapshot(
                    candidate,
                    infrastructure.ReadManifest(candidate),
                    isLocalDirectory: true
                )
            );
        }

        return infrastructure.StageAsync(tempRoot =>
        {
            var extractRoot = (tempRoot.AsAbsolutePath() / "extract").Value;
            extractRoot.AsAbsolutePath().CreateDirectory();
            infrastructure.ExtractArchive(candidate, extractRoot);
            return Task.FromResult(extractRoot);
        });
    }

    public RestoredPluginLocation Restore(
        string configRoot,
        PluginRequirement requirement,
        PluginSourceSnapshot source,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log
    )
    {
        if (source.IsLocalDirectory)
        {
            log?.Invoke($"Using local plugin directory '{source.PluginRoot}'.");
            return new RestoredPluginLocation(source.PluginRoot, true, true);
        }

        return infrastructure.RestoreToCache(configRoot, requirement, source, log, isLocal: true);
    }
}
