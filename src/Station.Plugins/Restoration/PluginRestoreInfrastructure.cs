using System.Net.Http.Json;
using System.Text.Json.Nodes;

using SharpCompress.Archives;
using SharpCompress.Common;

using Station.Abstractions.Configuration;
using Station.Abstractions.FileSystem;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;
using Station.Utilities.Processes;

using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Restoration;

internal sealed class PluginRestoreInfrastructure
{
    private readonly HttpClient httpClient;
    private readonly IProcessRunner processRunner;
    private readonly PluginManifestReader manifestReader;
    private readonly PluginCacheStore cacheStore;

    public PluginRestoreInfrastructure(
        IProcessRunner processRunner,
        PluginManifestReader manifestReader,
        PluginCacheStore cacheStore,
        HttpClient httpClient
    )
    {
        this.processRunner = processRunner;
        this.manifestReader = manifestReader;
        this.cacheStore = cacheStore;
        this.httpClient = httpClient;
    }

    public IProcessRunner ProcessRunner => processRunner;

    public string GetPluginsDirectory( string configRoot ) =>
    (
        configRoot.AsLocalPath().ResolveToCurrentDirectory() / PluginLayout.PluginsDirectoryName
    ).Value;

    public PluginManifest ReadManifest( string pluginRoot ) =>
        manifestReader.ReadManifest( pluginRoot );

    public async Task<PluginSourceSnapshot> StageAsync( Func<string, Task<string>> stageSourceAsync )
    {
        var tempRoot = Temporary.SystemTempDirectory() / "station" / Guid.NewGuid().ToString( "N" );
        tempRoot.CreateDirectory();
        try
        {
            var stagedRoot = await stageSourceAsync( tempRoot.Value );
            var pluginRoot = ResolvePluginRoot( stagedRoot );
            return new PluginSourceSnapshot( pluginRoot, ReadManifest( pluginRoot ), tempRoot );
        }
        catch
        {
            ClearAttributes( tempRoot );
            tempRoot.DeleteDirectoryRecursively();
            throw;
        }
    }

    public RestoredPluginLocation RestoreToCache(
        string configRoot,
        PluginRequirement requirement,
        PluginSourceSnapshot source,
        Action<string>? log,
        string? cacheRootOverride,
        bool isLocal = false
    ) => cacheStore.RestoreToCache( configRoot, requirement, source, log, cacheRootOverride, isLocal );

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync( cancellationToken );
        await using var destination = destinationPath.AsAbsolutePath().Create();
        await source.CopyToAsync( destination, cancellationToken );
    }

    public string ResolveLocalPluginCandidate( string pluginsRoot, string name )
    {
        var pluginName = name.AsLocalPath();
        if ( pluginName.IsAbsolute || pluginName.FileName != name )
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );

        var pluginDirectory = pluginsRoot.AsAbsolutePath();
        var directoryCandidate = pluginDirectory / name;
        if (
            directoryCandidate.Parent is not { } candidateParent
            || !candidateParent.Equals( pluginDirectory, AbsolutePath.PlatformDefaultComparer )
        )
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );

        var hasExtension = !string.IsNullOrEmpty( pluginName.GetExtensionWithDot() );
        if ( !hasExtension && directoryCandidate.ExistsDirectory() )
            return directoryCandidate.Value;

        if ( hasExtension )
            return directoryCandidate.ExistsFile() || directoryCandidate.ExistsDirectory()
                ? directoryCandidate.Value
                : string.Empty;

        var archiveCandidate = new[]
            {
                $"{directoryCandidate}.zip".AsAbsolutePath(),
                $"{directoryCandidate}.tar".AsAbsolutePath(),
                $"{directoryCandidate}.tar.gz".AsAbsolutePath(),
                $"{directoryCandidate}.tgz".AsAbsolutePath(),
                $"{directoryCandidate}.7z".AsAbsolutePath(),
            }
            .Where( static path => path.ExistsFile() )
            .Select( static path => path.Value )
            .FirstOrDefault();

        return archiveCandidate ?? string.Empty;
    }

    public async Task<string> ResolveNuGetPackageBaseAddressAsync(
        string feed,
        CancellationToken cancellationToken
    )
    {
        var serviceIndex =
            await httpClient.GetFromJsonAsync<JsonObject>( feed, cancellationToken )
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

    public void ExtractArchive( string archivePath, string destinationRoot )
    {
        using var archive = ArchiveFactory.OpenArchive( archivePath );
        foreach ( var entry in archive.Entries.Where( static entry => !entry.IsDirectory ) )
            entry.WriteToDirectory(
                destinationRoot,
                new ExtractionOptions { ExtractFullPath = true, Overwrite = true }
            );
    }

    public string ResolvePluginRoot( string candidateRoot ) =>
        manifestReader.ResolvePluginRoot( candidateRoot );

    public string SelectProcessError( ProcessResult result, string defaultMessage )
    {
        if ( !string.IsNullOrWhiteSpace( result.StandardError ) )
            return result.StandardError.Trim();

        if ( !string.IsNullOrWhiteSpace( result.StandardOutput ) )
            return result.StandardOutput.Trim();

        return defaultMessage;
    }

    internal static void ClearAttributes( AbsolutePath root ) =>
        PluginCacheStore.ClearAttributes( root );
}
