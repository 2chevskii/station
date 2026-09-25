using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Station.Core.Configuration;
using Station.Core.Configuration.Models;
using Station.Core.Plugins.Services;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Plugins.Providers;

internal sealed class NpmPluginProvider : IPluginProvider
{
    public string Name => "npm";

    public async Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        var registry = string.IsNullOrWhiteSpace(declaration.Registry)
            ? "https://registry.npmjs.org/"
            : declaration.Registry!;

        return await infrastructure.RestoreToCacheAsync(
            configRoot,
            declaration with
            {
                Registry = registry,
            },
            log,
            cancellationToken,
            async tempRoot =>
            {
                var metadataUrl =
                    $"{registry.TrimEnd('/')}/{Uri.EscapeDataString(declaration.Id!)}";
                var metadata = await GetMetadataAsync(metadataUrl, cancellationToken);

                var resolvedVersion = declaration.Version;
                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    resolvedVersion = metadata["dist-tags"]?["latest"]?.GetValue<string>();
                }

                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    throw new ConfigurationException(
                        $"npm registry '{registry}' did not resolve a version for package '{declaration.Id}'."
                    );
                }

                var tarballUrl = metadata["versions"]
                    ?[resolvedVersion]?["dist"]?["tarball"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(tarballUrl))
                {
                    throw new ConfigurationException(
                        $"npm registry '{registry}' did not return a tarball URL for '{declaration.Id}@{resolvedVersion}'."
                    );
                }

                var archivePath = (
                    new AbsolutePath(tempRoot) / new LocalPath(new Uri(tarballUrl).AbsolutePath).FileName
                ).Value;
                await infrastructure.DownloadFileAsync(tarballUrl, archivePath, cancellationToken);

                var extractRoot = (new AbsolutePath(tempRoot) / "extract").Value;
                new AbsolutePath(extractRoot).CreateDirectory();
                infrastructure.ExtractArchive(archivePath, extractRoot);
                return extractRoot;
            }
        );
    }

    private static async Task<JsonObject> GetMetadataAsync(
        string metadataUrl,
        CancellationToken cancellationToken
    )
    {
        using var client = new HttpClient();
        return await client.GetFromJsonAsync<JsonObject>(metadataUrl, cancellationToken)
            ?? throw new ConfigurationException(
                $"npm registry returned an empty metadata document for '{metadataUrl}'."
            );
    }
}
