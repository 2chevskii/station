using Station.Abstractions.FileSystem;
using Station.Abstractions.Processes;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Caching;
using Station.Plugins.Manifests;

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

    public HttpClient HttpClient => httpClient;

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
