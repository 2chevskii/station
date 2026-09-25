using Station.Core.Configuration;
using Station.Core.Configuration.Models;
using Station.Core.Plugins.Services;

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
                var archivePath = Path.Combine(
                    tempRoot,
                    Path.GetFileName(new Uri(declaration.Url!).AbsolutePath)
                );
                await infrastructure.DownloadFileAsync(
                    declaration.Url!,
                    archivePath,
                    cancellationToken
                );
                var extractRoot = Path.Combine(tempRoot, "extract");
                Directory.CreateDirectory(extractRoot);
                infrastructure.ExtractArchive(archivePath, extractRoot);
                return extractRoot;
            }
        );
    }
}
