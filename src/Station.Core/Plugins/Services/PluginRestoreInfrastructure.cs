using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NuGet.Versioning;
using SharpCompress.Archives;
using SharpCompress.Common;
using Station.Core.Configuration;
using Station.Core.Configuration.Models;
using Station.Core.Configuration.Services;
using Station.Core.Infrastructure.Processes;
using Station.Core.Plugins.Providers;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Plugins.Services;

internal sealed class PluginRestoreInfrastructure
{
    private const string CacheDirectoryName = ".station";
    private const string PluginCacheDirectoryName = "plugin_cache";

    private static readonly HttpClient HttpClient = new();
    private readonly LocalConfigurationDiscovery localConfigurationDiscovery;
    private readonly IProcessRunner processRunner;

    public PluginRestoreInfrastructure(
        LocalConfigurationDiscovery localConfigurationDiscovery,
        IProcessRunner processRunner
    )
    {
        this.localConfigurationDiscovery = localConfigurationDiscovery;
        this.processRunner = processRunner;
    }

    public IProcessRunner ProcessRunner => processRunner;

    public string GetPluginsDirectory(string configRoot) =>
        localConfigurationDiscovery.GetPluginsDirectory(configRoot);

    public async Task<RestoredPluginLocation> RestoreToCacheAsync(
        string configRoot,
        PluginDeclaration declaration,
        Action<string>? log,
        CancellationToken cancellationToken,
        Func<string, Task<string>> restoreIntoTempAsync,
        bool isLocal = false,
        string? cacheKeyOverride = null
    )
    {
        var cacheRoot = GetCacheRoot(configRoot);
        cacheRoot.CreateDirectory();

        var cacheKey = cacheKeyOverride ?? BuildCacheKey(declaration);
        var destinationRoot = cacheRoot / cacheKey;
        if (destinationRoot.ExistsDirectory())
        {
            var cachedPluginRoot = ResolvePluginRoot(destinationRoot.Value);
            log?.Invoke($"Using cached plugin '{declaration.DisplaySource}'.");
            return new RestoredPluginLocation(cachedPluginRoot, CacheHit: true, IsLocal: isLocal);
        }

        var tempRoot = Temporary.SystemTempDirectory() / "station" / Guid.NewGuid().ToString("N");
        tempRoot.CreateDirectory();

        try
        {
            log?.Invoke($"Restoring plugin '{declaration.DisplaySource}'.");
            var stagedRoot = await restoreIntoTempAsync(tempRoot.Value);
            var pluginRoot = ResolvePluginRoot(stagedRoot);
            CopyDirectory(new AbsolutePath(pluginRoot), destinationRoot);
            return new RestoredPluginLocation(
                ResolvePluginRoot(destinationRoot.Value),
                CacheHit: false,
                IsLocal: isLocal
            );
        }
        finally
        {
            if (tempRoot.ExistsDirectory())
            {
                ClearAttributes(tempRoot);
                tempRoot.DeleteDirectoryRecursively();
            }
        }
    }

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        using var response = await HttpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new AbsolutePath(destinationPath).Create();
        await source.CopyToAsync(destination, cancellationToken);
    }

    public string ResolveLocalPluginCandidate(string pluginsRoot, string name)
    {
        var pluginName = new LocalPath(name);
        if (pluginName.IsAbsolute || pluginName.FileName != name)
        {
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );
        }

        var pluginDirectory = new AbsolutePath(pluginsRoot);
        var directoryCandidate = pluginDirectory / name;
        if (directoryCandidate.Parent is not { } candidateParent
            || !candidateParent.Equals(pluginDirectory, AbsolutePath.PlatformDefaultComparer))
        {
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );
        }

        var hasExtension = !string.IsNullOrEmpty(pluginName.GetExtensionWithDot());
        if (!hasExtension && directoryCandidate.ExistsDirectory())
        {
            return directoryCandidate.Value;
        }

        if (hasExtension)
        {
            return directoryCandidate.ExistsFile() || directoryCandidate.ExistsDirectory()
                ? directoryCandidate.Value
                : string.Empty;
        }

        var archiveCandidate = new[]
        {
            new AbsolutePath($"{directoryCandidate}.zip"),
            new AbsolutePath($"{directoryCandidate}.tar"),
            new AbsolutePath($"{directoryCandidate}.tar.gz"),
            new AbsolutePath($"{directoryCandidate}.tgz"),
            new AbsolutePath($"{directoryCandidate}.7z"),
        }.Where(static path => path.ExistsFile()).Select(static path => path.Value).FirstOrDefault();

        return archiveCandidate ?? string.Empty;
    }

    public async Task<string> ResolveNuGetPackageBaseAddressAsync(
        string feed,
        CancellationToken cancellationToken
    )
    {
        var serviceIndex =
            await HttpClient.GetFromJsonAsync<JsonObject>(feed, cancellationToken)
            ?? throw new ConfigurationException(
                $"NuGet feed '{feed}' returned an empty service index."
            );

        var resources =
            serviceIndex["resources"] as JsonArray
            ?? throw new ConfigurationException(
                $"NuGet feed '{feed}' did not expose any resources."
            );
        foreach (var resource in resources.OfType<JsonObject>())
        {
            var type = resource["@type"]?.GetValue<string>();
            var id = resource["@id"]?.GetValue<string>();
            if (
                type is not null
                && id is not null
                && type.StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase)
            )
            {
                return id;
            }
        }

        throw new ConfigurationException(
            $"NuGet feed '{feed}' does not expose a PackageBaseAddress resource."
        );
    }

    public void ExtractArchive(string archivePath, string destinationRoot)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries.Where(static entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(
                destinationRoot,
                new ExtractionOptions { ExtractFullPath = true, Overwrite = true }
            );
        }
    }

    public string ResolvePluginRoot(string candidateRoot)
    {
        if (HasPluginManifest(candidateRoot))
        {
            return candidateRoot;
        }

        var manifests = new AbsolutePath(candidateRoot).GetFiles(
            "plugin-manifest.y*",
            SearchOption.AllDirectories
        );
        if (manifests.Length == 1)
        {
            return new AbsolutePath(manifests[0]).Parent!.Value.Value;
        }

        throw new ConfigurationException(
            $"Could not locate a unique plugin manifest under '{candidateRoot}'."
        );
    }

    public string BuildLocalArchiveCacheKey(string archivePath)
    {
        var fileInfo = new FileInfo(new AbsolutePath(archivePath).Value);
        return ComputeStableHash(
            $"{archivePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}"
        );
    }

    public string SelectProcessError(ProcessResult result, string defaultMessage)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError.Trim();
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput.Trim();
        }

        return defaultMessage;
    }

    private static bool HasPluginManifest(string root) =>
        (new AbsolutePath(root) / "plugin-manifest.yaml").ExistsFile()
        || (new AbsolutePath(root) / "plugin-manifest.yml").ExistsFile();

    private static AbsolutePath GetCacheRoot(string configRoot) =>
        new LocalPath(configRoot).ResolveToCurrentDirectory() / CacheDirectoryName / PluginCacheDirectoryName;

    private static string BuildCacheKey(PluginDeclaration declaration)
    {
        var builder = new StringBuilder()
            .Append(declaration.Provider)
            .Append('|')
            .Append(declaration.Name)
            .Append('|')
            .Append(declaration.Url)
            .Append('|')
            .Append(declaration.Ref)
            .Append('|')
            .Append(declaration.Id)
            .Append('|')
            .Append(declaration.Version)
            .Append('|')
            .Append(declaration.Feed)
            .Append('|')
            .Append(declaration.Registry);

        return ComputeStableHash(builder.ToString());
    }

    private static string ComputeStableHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void CopyDirectory(AbsolutePath sourceRoot, AbsolutePath destinationRoot)
    {
        destinationRoot.CreateDirectory();

        foreach (var path in sourceRoot.GetDirectories("*", SearchOption.AllDirectories))
        {
            var relativePath = new AbsolutePath(path).RelativeTo(sourceRoot);
            (destinationRoot / relativePath).CreateDirectory();
        }

        foreach (var path in sourceRoot.GetFiles("*", SearchOption.AllDirectories))
        {
            var file = new AbsolutePath(path);
            var destinationPath = destinationRoot / file.RelativeTo(sourceRoot);
            destinationPath.Parent!.Value.CreateDirectory();
            file.Copy(destinationPath, overwrite: true);
        }
    }

    private static void ClearAttributes(AbsolutePath root)
    {
        foreach (var path in root.GetDirectories("*", SearchOption.AllDirectories))
        {
            new AbsolutePath(path).SetAttributes(FileAttributes.Normal);
        }

        foreach (var path in root.GetFiles("*", SearchOption.AllDirectories))
        {
            new AbsolutePath(path).SetAttributes(FileAttributes.Normal);
        }

        root.SetAttributes(FileAttributes.Normal);
    }
}
