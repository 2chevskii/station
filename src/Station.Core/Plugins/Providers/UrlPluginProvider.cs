using Station.Core.Configuration;
using Station.Core.Configuration.Models;
using Station.Core.Plugins.Services;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Plugins.Providers;

internal sealed class UrlPluginProvider : IPluginProvider
{
    public string Name => "url";

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
                var archivePath = (
                    new AbsolutePath(tempRoot) / new LocalPath(new Uri(declaration.Url!).AbsolutePath).FileName
                ).Value;
                await infrastructure.DownloadFileAsync(
                    declaration.Url!,
                    archivePath,
                    cancellationToken
                );
                var extractRoot = (new AbsolutePath(tempRoot) / "extract").Value;
                new AbsolutePath(extractRoot).CreateDirectory();
                infrastructure.ExtractArchive(archivePath, extractRoot);
                return extractRoot;
            }
        );
    }
}
