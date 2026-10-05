using Station.Abstractions.Configuration;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Processes;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;

namespace Station.Plugins.Providers.Git;

internal sealed class GitPluginProvider( PluginRestoreInfrastructure infrastructure ) : IPluginProvider
{
    public string Name => "git";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        CancellationToken cancellationToken
    )
    {
        return infrastructure.StageAsync( async tempRoot =>
        {
            if ( !infrastructure.ProcessRunner.CommandExists( "git" ) )
                throw new ConfigurationException( "git is not available on PATH." );

            var cloneRoot = (tempRoot.AsAbsolutePath() / "clone").Value;
            var cloneResult = await infrastructure.ProcessRunner.RunAsync(
                new ProcessCommand( "git", ["clone", requirement.Url!, cloneRoot] ),
                cancellationToken
            );
            if ( !cloneResult.Succeeded )
                throw new ConfigurationException(
                    infrastructure.SelectProcessError( cloneResult, "git clone failed." )
                );

            if ( !string.IsNullOrWhiteSpace( requirement.Ref ) )
            {
                var checkoutResult = await infrastructure.ProcessRunner.RunAsync(
                    new ProcessCommand( "git", ["checkout", requirement.Ref!], cloneRoot ),
                    cancellationToken
                );
                if ( !checkoutResult.Succeeded )
                    throw new ConfigurationException(
                        infrastructure.SelectProcessError( checkoutResult, "git checkout failed." )
                    );
            }

            return cloneRoot;
        } );
    }

    public RestoredPluginLocation Restore(
        string configRoot,
        PluginRequirement requirement,
        PluginSourceSnapshot source,
        Action<string>? log,
        string? cacheRootOverride
    ) =>
        infrastructure.RestoreToCache( configRoot, requirement, source, log, cacheRootOverride );
}
