using Station.Core.Configuration;
using Station.Core.Configuration.Models;
using Station.Core.Infrastructure.Processes;
using Station.Core.Plugins.Services;

namespace Station.Core.Plugins.Providers;

internal sealed class GitPluginProvider : IPluginProvider
{
    public string Name => "git";

    public async Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        return await infrastructure.RestoreToCacheAsync(
            configRoot,
            declaration,
            log,
            cancellationToken,
            async tempRoot =>
            {
                if (!infrastructure.ProcessRunner.CommandExists("git"))
                {
                    throw new ConfigurationException("git is not available on PATH.");
                }

                var cloneRoot = Path.Combine(tempRoot, "clone");
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
                            infrastructure.SelectProcessError(
                                checkoutResult,
                                "git checkout failed."
                            )
                        );
                    }
                }

                return cloneRoot;
            }
        );
    }
}
