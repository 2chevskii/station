using Station.Abstractions.FileSystem;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Sources;
using Station.Plugins.Restoration;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;
using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Providers;

internal sealed class UrlPluginProvider : IPluginProvider
{
    public string Name => "url";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    )
    {
        return infrastructure.StageAsync(async tempRoot =>
        {
            var archivePath = (
                tempRoot.AsAbsolutePath()
                / new Uri(declaration.Url!).AbsolutePath.AsLocalPath().FileName
            ).Value;
            await infrastructure.DownloadFileAsync(
                declaration.Url!,
                archivePath,
                cancellationToken
            );
            var extractRoot = (tempRoot.AsAbsolutePath() / "extract").Value;
            extractRoot.AsAbsolutePath().CreateDirectory();
            infrastructure.ExtractArchive(archivePath, extractRoot);
            return extractRoot;
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
