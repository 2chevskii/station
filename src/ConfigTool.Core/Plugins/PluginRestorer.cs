using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NuGet.Versioning;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ConfigTool.Core;

internal sealed class PluginRestorer
{
    private const string CacheDirectoryName = ".configtool";
    private const string PluginCacheDirectoryName = "plugin_cache";

    private static readonly HttpClient HttpClient = new();
    private readonly ConfigLoader configLoader;
    private readonly IProcessRunner processRunner;

    public PluginRestorer(ConfigLoader configLoader, IProcessRunner processRunner)
    {
        this.configLoader = configLoader;
        this.processRunner = processRunner;
    }

    public async Task<PluginRestoreResult> RestoreAsync(
        string configRoot,
        IReadOnlyList<PluginDeclaration> declarations,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (declarations.Count == 0)
        {
            return new PluginRestoreResult(true, "No plugins declared.", [], [], []);
        }

        var diagnostics = new List<string>();
        var records = new List<PluginRestoreRecord>();
        var manifests = new List<ResolvedPluginManifest>();

        foreach (var declaration in declarations)
        {
            try
            {
                var restored = await RestoreSingleAsync(configRoot, declaration, log, cancellationToken);
                var manifest = configLoader.LoadPluginManifest(restored.PluginRoot);
                manifests.Add(manifest);
                records.Add(new PluginRestoreRecord(
                    declaration.DisplaySource,
                    manifest.Name,
                    manifest.Version,
                    manifest.PluginRoot,
                    restored.CacheHit,
                    restored.IsLocal));
            }
            catch (Exception exception) when (exception is ConfigurationException or IOException or HttpRequestException)
            {
                diagnostics.Add($"{declaration.DisplaySource}: {exception.Message}");
            }
        }

        if (diagnostics.Count > 0)
        {
            return new PluginRestoreResult(
                false,
                "Plugin restore failed.",
                diagnostics,
                records,
                manifests);
        }

        return new PluginRestoreResult(
            true,
            $"Restored {manifests.Count} plugin(s).",
            [],
            records,
            manifests);
    }

    private async Task<(string PluginRoot, bool CacheHit, bool IsLocal)> RestoreSingleAsync(
        string configRoot,
        PluginDeclaration declaration,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        return declaration.Provider switch
        {
            PluginProviderKind.Local => await ResolveLocalPluginAsync(configRoot, declaration, log, cancellationToken),
            PluginProviderKind.Git => await RestoreToCacheAsync(
                configRoot,
                declaration,
                log,
                cancellationToken,
                async tempRoot =>
                {
                    if (!processRunner.CommandExists("git"))
                    {
                        throw new ConfigurationException("git is not available on PATH.");
                    }

                    var cloneRoot = Path.Combine(tempRoot, "clone");
                    var cloneResult = await processRunner.RunAsync(
                        new ProcessCommand("git", ["clone", declaration.Url!, cloneRoot]),
                        cancellationToken);
                    if (!cloneResult.Succeeded)
                    {
                        throw new ConfigurationException(SelectProcessError(cloneResult, "git clone failed."));
                    }

                    if (!string.IsNullOrWhiteSpace(declaration.Ref))
                    {
                        var checkoutResult = await processRunner.RunAsync(
                            new ProcessCommand("git", ["checkout", declaration.Ref!], cloneRoot),
                            cancellationToken);
                        if (!checkoutResult.Succeeded)
                        {
                            throw new ConfigurationException(SelectProcessError(checkoutResult, "git checkout failed."));
                        }
                    }

                    return cloneRoot;
                }),
            PluginProviderKind.Url => await RestoreToCacheAsync(
                configRoot,
                declaration,
                log,
                cancellationToken,
                async tempRoot =>
                {
                    var archivePath = Path.Combine(tempRoot, Path.GetFileName(new Uri(declaration.Url!).AbsolutePath));
                    await DownloadFileAsync(declaration.Url!, archivePath, cancellationToken);
                    var extractRoot = Path.Combine(tempRoot, "extract");
                    Directory.CreateDirectory(extractRoot);
                    ExtractArchive(archivePath, extractRoot);
                    return extractRoot;
                }),
            PluginProviderKind.NuGet => await RestoreNuGetAsync(configRoot, declaration, log, cancellationToken),
            PluginProviderKind.Npm => await RestoreNpmAsync(configRoot, declaration, log, cancellationToken),
            _ => throw new ConfigurationException($"Unsupported plugin provider '{declaration.Provider}'."),
        };
    }

    private async Task<(string PluginRoot, bool CacheHit, bool IsLocal)> ResolveLocalPluginAsync(
        string configRoot,
        PluginDeclaration declaration,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var pluginsRoot = configLoader.GetPluginsDirectory(configRoot);
        var candidate = ResolveLocalPluginCandidate(pluginsRoot, declaration.Name!);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ConfigurationException(
                $"Local plugin '{declaration.Name}' was not found under '{pluginsRoot}'.");
        }

        if (Directory.Exists(candidate))
        {
            log?.Invoke($"Using local plugin directory '{candidate}'.");
            return (candidate, true, true);
        }

        return await RestoreToCacheAsync(
            configRoot,
            declaration with { DisplaySource = candidate, Name = Path.GetFileName(candidate) },
            log,
            cancellationToken,
            async tempRoot =>
            {
                var extractRoot = Path.Combine(tempRoot, "extract");
                Directory.CreateDirectory(extractRoot);
                ExtractArchive(candidate, extractRoot);
                await Task.CompletedTask;
                return extractRoot;
            },
            isLocal: true,
            cacheKeyOverride: BuildLocalArchiveCacheKey(candidate));
    }

    private async Task<(string PluginRoot, bool CacheHit, bool IsLocal)> RestoreNuGetAsync(
        string configRoot,
        PluginDeclaration declaration,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var feed = string.IsNullOrWhiteSpace(declaration.Feed)
            ? "https://api.nuget.org/v3/index.json"
            : declaration.Feed!;

        return await RestoreToCacheAsync(
            configRoot,
            declaration with { Feed = feed },
            log,
            cancellationToken,
            async tempRoot =>
            {
                var packageBaseAddress = await ResolveNuGetPackageBaseAddressAsync(feed, cancellationToken);
                var normalizedId = declaration.Id!.ToLowerInvariant();
                var resolvedVersion = declaration.Version;
                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    var versionsUri = $"{packageBaseAddress.TrimEnd('/')}/{normalizedId}/index.json";
                    var versionsNode = await HttpClient.GetFromJsonAsync<JsonObject>(versionsUri, cancellationToken)
                        ?? throw new ConfigurationException($"NuGet feed '{feed}' returned an empty version index.");

                    resolvedVersion = versionsNode["versions"] is JsonArray versionsArray
                        ? versionsArray.Select(node => node?.GetValue<string>())
                            .Where(static value => !string.IsNullOrWhiteSpace(value))
                            .Select(static value => NuGetVersion.Parse(value!))
                            .OrderByDescending(static version => version)
                            .FirstOrDefault()
                            ?.ToNormalizedString()
                        : null;

                    if (string.IsNullOrWhiteSpace(resolvedVersion))
                    {
                        throw new ConfigurationException(
                            $"NuGet feed '{feed}' did not return any versions for package '{declaration.Id}'.");
                    }
                }

                var packageFileName = $"{normalizedId}.{resolvedVersion!.ToLowerInvariant()}.nupkg";
                var packageUri =
                    $"{packageBaseAddress.TrimEnd('/')}/{normalizedId}/{resolvedVersion.ToLowerInvariant()}/{packageFileName}";
                var packagePath = Path.Combine(tempRoot, packageFileName);
                await DownloadFileAsync(packageUri, packagePath, cancellationToken);

                var extractRoot = Path.Combine(tempRoot, "extract");
                Directory.CreateDirectory(extractRoot);
                ExtractArchive(packagePath, extractRoot);
                return extractRoot;
            });
    }

    private async Task<(string PluginRoot, bool CacheHit, bool IsLocal)> RestoreNpmAsync(
        string configRoot,
        PluginDeclaration declaration,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var registry = string.IsNullOrWhiteSpace(declaration.Registry)
            ? "https://registry.npmjs.org/"
            : declaration.Registry!;

        return await RestoreToCacheAsync(
            configRoot,
            declaration with { Registry = registry },
            log,
            cancellationToken,
            async tempRoot =>
            {
                var metadataUrl = $"{registry.TrimEnd('/')}/{Uri.EscapeDataString(declaration.Id!)}";
                var metadata = await HttpClient.GetFromJsonAsync<JsonObject>(metadataUrl, cancellationToken)
                    ?? throw new ConfigurationException($"npm registry '{registry}' returned an empty metadata document.");

                var resolvedVersion = declaration.Version;
                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    resolvedVersion = metadata["dist-tags"]?["latest"]?.GetValue<string>();
                }

                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    throw new ConfigurationException(
                        $"npm registry '{registry}' did not resolve a version for package '{declaration.Id}'.");
                }

                var tarballUrl = metadata["versions"]?[resolvedVersion]?["dist"]?["tarball"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(tarballUrl))
                {
                    throw new ConfigurationException(
                        $"npm registry '{registry}' did not return a tarball URL for '{declaration.Id}@{resolvedVersion}'.");
                }

                var archivePath = Path.Combine(tempRoot, Path.GetFileName(new Uri(tarballUrl).AbsolutePath));
                await DownloadFileAsync(tarballUrl, archivePath, cancellationToken);

                var extractRoot = Path.Combine(tempRoot, "extract");
                Directory.CreateDirectory(extractRoot);
                ExtractArchive(archivePath, extractRoot);
                return extractRoot;
            });
    }

    private async Task<(string PluginRoot, bool CacheHit, bool IsLocal)> RestoreToCacheAsync(
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
            return (cachedPluginRoot, true, isLocal);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "config-tool", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            log?.Invoke($"Restoring plugin '{declaration.DisplaySource}'.");
            var stagedRoot = await restoreIntoTempAsync(tempRoot);
            var pluginRoot = ResolvePluginRoot(stagedRoot);
            CopyDirectory(pluginRoot, destinationRoot);
            return (ResolvePluginRoot(destinationRoot), false, isLocal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static string ResolveLocalPluginCandidate(string pluginsRoot, string name)
    {
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ConfigurationException("Local plugin names must stay within the root 'plugins' directory.");
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

    private static async Task<string> ResolveNuGetPackageBaseAddressAsync(string feed, CancellationToken cancellationToken)
    {
        var serviceIndex = await HttpClient.GetFromJsonAsync<JsonObject>(feed, cancellationToken)
            ?? throw new ConfigurationException($"NuGet feed '{feed}' returned an empty service index.");

        var resources = serviceIndex["resources"] as JsonArray
            ?? throw new ConfigurationException($"NuGet feed '{feed}' did not expose any resources.");
        foreach (var resource in resources.OfType<JsonObject>())
        {
            var type = resource["@type"]?.GetValue<string>();
            var id = resource["@id"]?.GetValue<string>();
            if (type is not null &&
                id is not null &&
                type.StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        throw new ConfigurationException(
            $"NuGet feed '{feed}' does not expose a PackageBaseAddress resource.");
    }

    private static void ExtractArchive(string archivePath, string destinationRoot)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries.Where(static entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(destinationRoot, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
            });
        }
    }

    private static string ResolvePluginRoot(string candidateRoot)
    {
        if (HasPluginManifest(candidateRoot))
        {
            return candidateRoot;
        }

        var manifests = Directory.GetFiles(candidateRoot, "plugin-manifest.y*", SearchOption.AllDirectories);
        if (manifests.Length == 1)
        {
            return Path.GetDirectoryName(manifests[0])!;
        }

        throw new ConfigurationException(
            $"Could not locate a unique plugin manifest under '{candidateRoot}'.");
    }

    private static bool HasPluginManifest(string root) =>
        File.Exists(Path.Combine(root, "plugin-manifest.yaml")) ||
        File.Exists(Path.Combine(root, "plugin-manifest.yml"));

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

    private static string BuildLocalArchiveCacheKey(string archivePath)
    {
        var fileInfo = new FileInfo(archivePath);
        return ComputeStableHash($"{archivePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}");
    }

    private static string ComputeStableHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
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

    private static string SelectProcessError(ProcessResult result, string defaultMessage)
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
}
