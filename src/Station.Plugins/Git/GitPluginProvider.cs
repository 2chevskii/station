using Station.Abstractions.FileSystem;
using Station.Abstractions.Models;
using Station.Plugins.Abstractions;
using Station.Plugins.Services;
using Station.Utilities.Processes;
using TruePath;

namespace Station.Plugins.Providers;

internal sealed class GitPluginProvider : IPluginProvider
{
    public string Name => "git";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginDeclaration declaration,
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
                new ProcessCommand("git", ["clone", declaration.Url!, cloneRoot]),
                cancellationToken
            );
            if (!cloneResult.Succeeded)
            {
                throw new ConfigurationException(
                    infrastructure.SelectProcessError(cloneResult, "git clone failed.")
                );
            }

            if (!string.IsNullOrWhiteSpace(declaration.Ref))
            {
                var checkoutResult = await infrastructure.ProcessRunner.RunAsync(
                    new ProcessCommand("git", ["checkout", declaration.Ref!], cloneRoot),
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
        PluginDeclaration declaration,
        PluginSourceSnapshot source,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log
    )
    {
        return infrastructure.RestoreToCache(configRoot, declaration, source, log);
    }
}
