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
        IProcessRunner processRunner)
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
        string? cacheKeyOverride = null)
    {
        var cacheRoot = GetCacheRoot(configRoot);
        Directory.CreateDirectory(cacheRoot);

        var cacheKey = cacheKeyOverride ?? BuildCacheKey(declaration);
        var destinationRoot = Path.Combine(cacheRoot, cacheKey);
        if (Directory.Exists(destinationRoot))
        {
            var cachedPluginRoot = ResolvePluginRoot(destinationRoot);
            log?.Invoke($"Using cached plugin '{declaration.DisplaySource}'.");
            return new RestoredPluginLocation(cachedPluginRoot, CacheHit: true, IsLocal: isLocal);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "station", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            log?.Invoke($"Restoring plugin '{declaration.DisplaySource}'.");
            var stagedRoot = await restoreIntoTempAsync(tempRoot);
            var pluginRoot = ResolvePluginRoot(stagedRoot);
            CopyDirectory(pluginRoot, destinationRoot);
            return new RestoredPluginLocation(
                ResolvePluginRoot(destinationRoot),
                CacheHit: false,
                IsLocal: isLocal
            );
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public string ResolveLocalPluginCandidate(string pluginsRoot, string name)
    {
        if (
            Path.IsPathRooted(name)
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar)
        )
        {
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );
        }

        var directoryCandidate = Path.Combine(pluginsRoot, name);
        if (!Path.HasExtension(name) && Directory.Exists(directoryCandidate))
        {
            return directoryCandidate;
        }

        if (Path.HasExtension(name))
        {
            var exactCandidate = Path.Combine(pluginsRoot, name);
            return File.Exists(exactCandidate) || Directory.Exists(exactCandidate)
                ? exactCandidate
                : string.Empty;
        }

        var archiveCandidate = new[]
        {
            $"{directoryCandidate}.zip",
            $"{directoryCandidate}.tar",
            $"{directoryCandidate}.tar.gz",
            $"{directoryCandidate}.tgz",
            $"{directoryCandidate}.7z",
        }.FirstOrDefault(File.Exists);

        return archiveCandidate ?? string.Empty;
    }

    public async Task<string> ResolveNuGetPackageBaseAddressAsync(
        string feed,
        CancellationToken cancellationToken)
    {
        var serviceIndex = await HttpClient.GetFromJsonAsync<JsonObject>(feed, cancellationToken)
            ?? throw new ConfigurationException(
                $"NuGet feed '{feed}' returned an empty service index."
            );

        var resources = serviceIndex["resources"] as JsonArray
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
        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries.Where(static entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(
                destinationRoot,
                new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true,
                }
            );
        }
    }

    public string ResolvePluginRoot(string candidateRoot)
    {
        if (HasPluginManifest(candidateRoot))
        {
            return candidateRoot;
        }

        var manifests = Directory.GetFiles(
            candidateRoot,
            "plugin-manifest.y*",
            SearchOption.AllDirectories
        );
        if (manifests.Length == 1)
        {
            return Path.GetDirectoryName(manifests[0])!;
        }

        throw new ConfigurationException(
            $"Could not locate a unique plugin manifest under '{candidateRoot}'."
        );
    }

    public string BuildLocalArchiveCacheKey(string archivePath)
    {
        var fileInfo = new FileInfo(archivePath);
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
        File.Exists(Path.Combine(root, "plugin-manifest.yaml"))
        || File.Exists(Path.Combine(root, "plugin-manifest.yml"));

    private static string GetCacheRoot(string configRoot) =>
        Path.Combine(Path.GetFullPath(configRoot), CacheDirectoryName, PluginCacheDirectoryName);

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

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (
            var directory in Directory.GetDirectories(
                sourceRoot,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }
}
