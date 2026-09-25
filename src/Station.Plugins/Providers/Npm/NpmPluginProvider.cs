using System.Net.Http.Json;
using System.Text.Json.Nodes;
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

internal sealed class NpmPluginProvider : IPluginProvider
{
    public string Name => "npm";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    )
    {
        var registry = string.IsNullOrWhiteSpace(requirement.Registry)
            ? "https://registry.npmjs.org/"
            : requirement.Registry!;

        return infrastructure.StageAsync(async tempRoot =>
        {
            var metadataUrl = $"{registry.TrimEnd('/')}/{Uri.EscapeDataString(requirement.Id!)}";
            var metadata = await GetMetadataAsync(metadataUrl, cancellationToken);

            var resolvedVersion = requirement.Version;
            if (string.IsNullOrWhiteSpace(resolvedVersion))
            {
                resolvedVersion = metadata["dist-tags"]?["latest"]?.GetValue<string>();
            }

            if (string.IsNullOrWhiteSpace(resolvedVersion))
            {
                throw new ConfigurationException(
                    $"npm registry '{registry}' did not resolve a version for package '{requirement.Id}'."
                );
            }

            var tarballUrl = metadata["versions"]
                ?[resolvedVersion]?["dist"]?["tarball"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(tarballUrl))
            {
                throw new ConfigurationException(
                    $"npm registry '{registry}' did not return a tarball URL for '{requirement.Id}@{resolvedVersion}'."
                );
            }

            var archivePath = (
                tempRoot.AsAbsolutePath() / new Uri(tarballUrl).AbsolutePath.AsLocalPath().FileName
            ).Value;
            await infrastructure.DownloadFileAsync(tarballUrl, archivePath, cancellationToken);

            var extractRoot = (tempRoot.AsAbsolutePath() / "extract").Value;
            extractRoot.AsAbsolutePath().CreateDirectory();
            infrastructure.ExtractArchive(archivePath, extractRoot);
            return extractRoot;
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
        var registry = string.IsNullOrWhiteSpace(requirement.Registry)
            ? "https://registry.npmjs.org/"
            : requirement.Registry!;
        return infrastructure.RestoreToCache(
            configRoot,
            requirement with
            {
                Registry = registry,
            },
            source,
            log
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
