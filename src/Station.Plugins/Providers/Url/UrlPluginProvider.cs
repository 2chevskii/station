using Station.Abstractions.FileSystem;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;

using TruePath.SystemIo;

namespace Station.Plugins.Providers.Url;

internal sealed class UrlPluginProvider( PluginRestoreInfrastructure infrastructure ) : IPluginProvider
{
    public string Name => "url";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        CancellationToken cancellationToken
    )
    {
        return infrastructure.StageAsync( async tempRoot =>
        {
            var archivePath = (
                tempRoot.AsAbsolutePath()
                / new Uri( requirement.Url! ).AbsolutePath.AsLocalPath().FileName
            ).Value;
            await infrastructure.DownloadFileAsync(
                requirement.Url!,
                archivePath,
                cancellationToken
            );
            var extractRoot = (tempRoot.AsAbsolutePath() / "extract").Value;
            extractRoot.AsAbsolutePath().CreateDirectory();
            PluginArchiveExtractor.Extract( archivePath, extractRoot );
            return extractRoot;
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
