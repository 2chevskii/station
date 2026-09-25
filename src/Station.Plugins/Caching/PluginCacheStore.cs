using System.Security.Cryptography;
using System.Text;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Sources;
using Station.Plugins.Providers;
using Station.Plugins.Restoration;
using Station.Plugins.Manifests;
using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Caching;

internal sealed class PluginCacheStore(PluginManifestReader manifestReader)
{
    private const string CacheDirectoryName = ".station";
    private const string PluginCacheDirectoryName = "plugin_cache";

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
                var cachedPluginRoot = manifestReader.ResolvePluginRoot(destinationRoot.Value);
                var cachedManifest = manifestReader.ReadManifest(cachedPluginRoot);
                if (
                    string.Equals(
                        cachedManifest.Name!.Trim(),
                        source.Manifest.Name!.Trim(),
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        cachedManifest.Version!.Trim(),
                        source.Manifest.Version!.Trim(),
                        StringComparison.Ordinal
                    )
                )
                {
                    log?.Invoke($"Using cached plugin '{declaration.DisplaySource}'.");
                    return new RestoredPluginLocation(
                        cachedPluginRoot,
                        CacheHit: true,
                        IsLocal: isLocal
                    );
                }
            }
            catch (Exception exception) when (exception is ConfigurationException or IOException)
            {
                log?.Invoke(
                    $"Refreshing invalid cached plugin '{declaration.DisplaySource}': {exception.Message}"
                );
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
                var installedRoot = manifestReader.ResolvePluginRoot(destinationRoot.Value);
                _ = manifestReader.ReadManifest(installedRoot);
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

    private static AbsolutePath GetCacheRoot(string configRoot) =>
        configRoot.AsLocalPath().ResolveToCurrentDirectory()
        / CacheDirectoryName
        / PluginCacheDirectoryName;

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
