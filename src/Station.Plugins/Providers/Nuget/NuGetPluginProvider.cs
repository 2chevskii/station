using System.Net.Http.Json;
using System.Text.Json.Nodes;

using NuGet.Versioning;

using Station.Abstractions.Configuration;
using Station.Abstractions.FileSystem;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;

using TruePath.SystemIo;

namespace Station.Plugins.Providers.Nuget;

internal sealed class NuGetPluginProvider(
    PluginRestoreInfrastructure infrastructure,
    HttpClient httpClient
) : IPluginProvider
{
    public string Name => "nuget";

    public Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginRequirement requirement,
        CancellationToken cancellationToken
    )
    {
        var feed = string.IsNullOrWhiteSpace( requirement.Feed )
            ? "https://api.nuget.org/v3/index.json"
            : requirement.Feed!;

        return infrastructure.StageAsync( async tempRoot =>
        {
            var packageBaseAddress = await ResolvePackageBaseAddressAsync(
                feed,
                cancellationToken
            );
            var normalizedId = requirement.Id!.ToLowerInvariant();
            var resolvedVersion = requirement.Version;
            if ( string.IsNullOrWhiteSpace( resolvedVersion ) )
            {
                var versionsUri = $"{packageBaseAddress.TrimEnd( '/' )}/{normalizedId}/index.json";
                var versionsNode = await GetVersionsAsync( versionsUri, cancellationToken );

                resolvedVersion = versionsNode["versions"] is JsonArray versionsArray
                    ? versionsArray
                        .Select( node => node?.GetValue<string>() )
                        .Where( static value => !string.IsNullOrWhiteSpace( value ) )
                        .Select( static value => NuGetVersion.Parse( value! ) )
                        .OrderByDescending( static version => version )
                        .FirstOrDefault()
                        ?.ToNormalizedString()
                    : null;

                if ( string.IsNullOrWhiteSpace( resolvedVersion ) )
                    throw new ConfigurationException(
                        $"NuGet feed '{feed}' did not return any versions for package '{requirement.Id}'."
                    );
            }

            var packageFileName = $"{normalizedId}.{resolvedVersion.ToLowerInvariant()}.nupkg";
            var packageUri =
                $"{packageBaseAddress.TrimEnd( '/' )}/{normalizedId}/{resolvedVersion.ToLowerInvariant()}/{packageFileName}";
            var packagePath = (tempRoot.AsAbsolutePath() / packageFileName).Value;
            await infrastructure.DownloadFileAsync( packageUri, packagePath, cancellationToken );

            var extractRoot = (tempRoot.AsAbsolutePath() / "extract").Value;
            extractRoot.AsAbsolutePath().CreateDirectory();
            PluginArchiveExtractor.Extract( packagePath, extractRoot );
            return extractRoot;
        } );
    }

    public RestoredPluginLocation Restore(
        string configRoot,
        PluginRequirement requirement,
        PluginSourceSnapshot source,
        Action<string>? log,
        string? cacheRootOverride
    )
    {
        var feed = string.IsNullOrWhiteSpace( requirement.Feed )
            ? "https://api.nuget.org/v3/index.json"
            : requirement.Feed!;
        return infrastructure.RestoreToCache(
            configRoot,
            requirement with
            {
                Feed = feed,
            },
            source,
            log,
            cacheRootOverride
        );
    }

    private async Task<string> ResolvePackageBaseAddressAsync(
        string feed,
        CancellationToken cancellationToken
    )
    {
        var serviceIndex =
            await infrastructure.HttpClient.GetFromJsonAsync<JsonObject>( feed, cancellationToken )
            ?? throw new ConfigurationException(
                $"NuGet feed '{feed}' returned an empty service index."
            );

        var resources =
            serviceIndex["resources"] as JsonArray
            ?? throw new ConfigurationException(
                $"NuGet feed '{feed}' did not expose any resources."
            );
        foreach ( var resource in resources.OfType<JsonObject>() )
        {
            var type = resource["@type"]?.GetValue<string>();
            var id = resource["@id"]?.GetValue<string>();
            if (
                type is not null
                && id is not null
                && type.StartsWith( "PackageBaseAddress", StringComparison.OrdinalIgnoreCase )
            )
                return id;
        }

        throw new ConfigurationException(
            $"NuGet feed '{feed}' does not expose a PackageBaseAddress resource."
        );
    }

    private async Task<JsonObject> GetVersionsAsync(
        string versionsUri,
        CancellationToken cancellationToken
    )
    {
        return await httpClient.GetFromJsonAsync<JsonObject>( versionsUri, cancellationToken )
               ?? throw new ConfigurationException(
                   $"NuGet feed returned an empty version index for '{versionsUri}'."
               );
    }
}
