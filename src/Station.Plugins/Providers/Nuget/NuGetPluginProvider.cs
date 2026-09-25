using System.Net.Http.Json;
using System.Text.Json.Nodes;
using NuGet.Versioning;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Sources;
using Station.Plugins.Restoration;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;
using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Providers;

internal sealed class NuGetPluginProvider : IPluginProvider
{
    public string Name => "nuget";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    )
    {
        var feed = string.IsNullOrWhiteSpace(declaration.Feed)
            ? "https://api.nuget.org/v3/index.json"
            : declaration.Feed!;

        return infrastructure.StageAsync(async tempRoot =>
        {
            var packageBaseAddress = await infrastructure.ResolveNuGetPackageBaseAddressAsync(
                feed,
                cancellationToken
            );
            var normalizedId = declaration.Id!.ToLowerInvariant();
            var resolvedVersion = declaration.Version;
            if (string.IsNullOrWhiteSpace(resolvedVersion))
            {
                var versionsUri = $"{packageBaseAddress.TrimEnd('/')}/{normalizedId}/index.json";
                var versionsNode = await GetVersionsAsync(versionsUri, cancellationToken);

                resolvedVersion = versionsNode["versions"] is JsonArray versionsArray
                    ? versionsArray
                        .Select(node => node?.GetValue<string>())
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => NuGetVersion.Parse(value!))
                        .OrderByDescending(static version => version)
                        .FirstOrDefault()
                        ?.ToNormalizedString()
                    : null;

                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    throw new ConfigurationException(
                        $"NuGet feed '{feed}' did not return any versions for package '{declaration.Id}'."
                    );
                }
            }

            var packageFileName = $"{normalizedId}.{resolvedVersion!.ToLowerInvariant()}.nupkg";
            var packageUri =
                $"{packageBaseAddress.TrimEnd('/')}/{normalizedId}/{resolvedVersion.ToLowerInvariant()}/{packageFileName}";
            var packagePath = (tempRoot.AsAbsolutePath() / packageFileName).Value;
            await infrastructure.DownloadFileAsync(packageUri, packagePath, cancellationToken);

            var extractRoot = (tempRoot.AsAbsolutePath() / "extract").Value;
            extractRoot.AsAbsolutePath().CreateDirectory();
            infrastructure.ExtractArchive(packagePath, extractRoot);
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
        var feed = string.IsNullOrWhiteSpace(declaration.Feed)
            ? "https://api.nuget.org/v3/index.json"
            : declaration.Feed!;
        return infrastructure.RestoreToCache(
            configRoot,
            declaration with
            {
                Feed = feed,
            },
            source,
            log
        );
    }

    private static async Task<JsonObject> GetVersionsAsync(
        string versionsUri,
        CancellationToken cancellationToken
    )
    {
        using var client = new HttpClient();
        return await client.GetFromJsonAsync<JsonObject>(versionsUri, cancellationToken)
            ?? throw new ConfigurationException(
                $"NuGet feed returned an empty version index for '{versionsUri}'."
            );
    }
}
