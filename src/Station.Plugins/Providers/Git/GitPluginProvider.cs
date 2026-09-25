using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;
using Station.Utilities.Processes;
using TruePath;

namespace Station.Plugins.Providers;

internal sealed class GitPluginProvider : IPluginProvider
{
    public string Name => "git";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    )
    {
        return infrastructure.StageAsync(async tempRoot =>
        {
            if (!infrastructure.ProcessRunner.CommandExists("git"))
            {
                throw new ConfigurationException("git is not available on PATH.");
            }

            var cloneRoot = (tempRoot.AsAbsolutePath() / "clone").Value;
            var cloneResult = await infrastructure.ProcessRunner.RunAsync(
                new ProcessCommand("git", ["clone", requirement.Url!, cloneRoot]),
                cancellationToken
            );
            if (!cloneResult.Succeeded)
            {
                throw new ConfigurationException(
                    infrastructure.SelectProcessError(cloneResult, "git clone failed.")
                );
            }

            if (!string.IsNullOrWhiteSpace(requirement.Ref))
            {
                var checkoutResult = await infrastructure.ProcessRunner.RunAsync(
                    new ProcessCommand("git", ["checkout", requirement.Ref!], cloneRoot),
                    cancellationToken
                );
                if (!checkoutResult.Succeeded)
                {
                    throw new ConfigurationException(
                        infrastructure.SelectProcessError(checkoutResult, "git checkout failed.")
                    );
                }
            }

            return cloneRoot;
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
        return infrastructure.RestoreToCache(configRoot, requirement, source, log);
    }
}
