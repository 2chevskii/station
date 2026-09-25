using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using NuGet.Versioning;
using SharpCompress.Archives;
using SharpCompress.Common;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Models;
using Station.Configuration.Yaml;
using Station.Plugins.Abstractions;
using Station.Plugins.Providers;
using Station.Utilities.Processes;
using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Services;

internal sealed class PluginRestoreInfrastructure
{
    private const string CacheDirectoryName = ".station";
    private const string PluginCacheDirectoryName = "plugin_cache";

    private static readonly HttpClient HttpClient = new();
    private readonly IProcessRunner processRunner;

    public PluginRestoreInfrastructure(IProcessRunner processRunner)
    {
        this.processRunner = processRunner;
    }

    public IProcessRunner ProcessRunner => processRunner;

    public string GetPluginsDirectory(string configRoot) =>
        (configRoot.AsLocalPath().ResolveToCurrentDirectory() / "plugins").Value;

    public PluginManifest ReadManifest(string pluginRoot)
    {
        var root = pluginRoot.AsAbsolutePath();
        var yamlPath = root / "plugin-manifest.yaml";
        var ymlPath = root / "plugin-manifest.yml";
        if (yamlPath.ExistsFile() && ymlPath.ExistsFile())
        {
            throw new ConfigurationException($"Plugin root '{root}' contains both manifest files.");
        }

        var manifestPath = yamlPath.ExistsFile() ? yamlPath
            : ymlPath.ExistsFile() ? ymlPath
            : throw new ConfigurationException($"Plugin root '{root}' has no plugin manifest.");
        var manifest = new YamlConfigurationReader().Load<PluginManifest>(manifestPath.Value);
        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' must declare a name and version."
            );
        }

        return manifest;
    }

    public async Task<PluginSourceSnapshot> StageAsync(Func<string, Task<string>> stageSourceAsync)
    {
        var tempRoot = Temporary.SystemTempDirectory() / "station" / Guid.NewGuid().ToString("N");
        tempRoot.CreateDirectory();
        try
        {
            var stagedRoot = await stageSourceAsync(tempRoot.Value);
            var pluginRoot = ResolvePluginRoot(stagedRoot);
            return new PluginSourceSnapshot(pluginRoot, ReadManifest(pluginRoot), tempRoot);
        }
        catch
        {
            ClearAttributes(tempRoot);
            tempRoot.DeleteDirectoryRecursively();
            throw;
        }
    }

    public RestoredPluginLocation RestoreToCache(
        string configRoot,
        PluginDeclaration declaration,
        PluginSourceSnapshot source,
        Action<string>? log,
        bool isLocal = false
    )
    {
        var cacheRoot = GetCacheRoot(configRoot);
        cacheRoot.CreateDirectory();

        var cacheKey = BuildCacheKey(declaration);
        var destinationRoot = cacheRoot / cacheKey;
        if (destinationRoot.ExistsDirectory())
        {
            try
            {
                var cachedPluginRoot = ResolvePluginRoot(destinationRoot.Value);
                var cachedManifest = ReadManifest(cachedPluginRoot);
                if (string.Equals(
                        cachedManifest.Name!.Trim(),
                        source.Manifest.Name!.Trim(),
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        cachedManifest.Version!.Trim(),
                        source.Manifest.Version!.Trim(),
                        StringComparison.Ordinal
                    ))
                {
                    log?.Invoke($"Using cached plugin '{declaration.DisplaySource}'.");
                    return new RestoredPluginLocation(cachedPluginRoot, CacheHit: true, IsLocal: isLocal);
                }
            }
            catch (Exception exception) when (exception is ConfigurationException or IOException)
            {
                log?.Invoke($"Refreshing invalid cached plugin '{declaration.DisplaySource}': {exception.Message}");
            }
        }

        var stagingRoot = cacheRoot / $"{cacheKey}.staging.{Guid.NewGuid():N}";
        var backupRoot = cacheRoot / $"{cacheKey}.backup.{Guid.NewGuid():N}";
        try
        {
            log?.Invoke($"Restoring plugin '{declaration.DisplaySource}'.");
            CopyDirectory(source.PluginRoot.AsAbsolutePath(), stagingRoot);
            if (destinationRoot.ExistsDirectory())
            {
                Directory.Move(destinationRoot.Value, backupRoot.Value);
            }

            try
            {
                Directory.Move(stagingRoot.Value, destinationRoot.Value);
                var installedRoot = ResolvePluginRoot(destinationRoot.Value);
                _ = ReadManifest(installedRoot);
                return new RestoredPluginLocation(installedRoot, CacheHit: false, IsLocal: isLocal);
            }
            catch
            {
                if (destinationRoot.ExistsDirectory())
                {
                    ClearAttributes(destinationRoot);
                    destinationRoot.DeleteDirectoryRecursively();
                }

                if (backupRoot.ExistsDirectory())
                {
                    Directory.Move(backupRoot.Value, destinationRoot.Value);
                }

                throw;
            }
        }
        finally
        {
            if (stagingRoot.ExistsDirectory())
            {
                ClearAttributes(stagingRoot);
                stagingRoot.DeleteDirectoryRecursively();
            }

            if (backupRoot.ExistsDirectory() && destinationRoot.ExistsDirectory())
            {
                ClearAttributes(backupRoot);
                backupRoot.DeleteDirectoryRecursively();
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
        await using var destination = destinationPath.AsAbsolutePath().Create();
        await source.CopyToAsync(destination, cancellationToken);
    }

    public string ResolveLocalPluginCandidate(string pluginsRoot, string name)
    {
        var pluginName = name.AsLocalPath();
        if (pluginName.IsAbsolute || pluginName.FileName != name)
        {
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );
        }

        var pluginDirectory = pluginsRoot.AsAbsolutePath();
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
            $"{directoryCandidate}.zip".AsAbsolutePath(),
            $"{directoryCandidate}.tar".AsAbsolutePath(),
            $"{directoryCandidate}.tar.gz".AsAbsolutePath(),
            $"{directoryCandidate}.tgz".AsAbsolutePath(),
            $"{directoryCandidate}.7z".AsAbsolutePath(),
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

        var manifests = candidateRoot.AsAbsolutePath().GetFiles(
            "plugin-manifest.y*",
            SearchOption.AllDirectories
        );
        if (manifests.Length == 1)
        {
            return manifests[0].AsAbsolutePath().Parent!.Value.Value;
        }

        throw new ConfigurationException(
            $"Could not locate a unique plugin manifest under '{candidateRoot}'."
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
        (root.AsAbsolutePath() / "plugin-manifest.yaml").ExistsFile()
        || (root.AsAbsolutePath() / "plugin-manifest.yml").ExistsFile();

    private static AbsolutePath GetCacheRoot(string configRoot) =>
        configRoot.AsLocalPath().ResolveToCurrentDirectory() / CacheDirectoryName / PluginCacheDirectoryName;

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
            var relativePath = path.AsAbsolutePath().RelativeTo(sourceRoot);
            (destinationRoot / relativePath).CreateDirectory();
        }

        foreach (var path in sourceRoot.GetFiles("*", SearchOption.AllDirectories))
        {
            var file = path.AsAbsolutePath();
            var destinationPath = destinationRoot / file.RelativeTo(sourceRoot);
            destinationPath.Parent!.Value.CreateDirectory();
            file.Copy(destinationPath, overwrite: true);
        }
    }

    internal static void ClearAttributes(AbsolutePath root)
    {
        foreach (var path in root.GetDirectories("*", SearchOption.AllDirectories))
        {
            path.AsAbsolutePath().SetAttributes(FileAttributes.Normal);
        }

        foreach (var path in root.GetFiles("*", SearchOption.AllDirectories))
        {
            path.AsAbsolutePath().SetAttributes(FileAttributes.Normal);
        }

        root.SetAttributes(FileAttributes.Normal);
    }
}
