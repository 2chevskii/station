using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;
using Station.Plugins.Abstractions.Requirements;
using Station.Plugins.Restoration;
using Station.Plugins.Manifests;
using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Caching;

internal sealed class PluginCacheStore(
    PluginManifestReader manifestReader,
    string? cacheRootOverride = null
)
{
    private const string CacheDirectoryName = ".station";
    private const string PluginCacheDirectoryName = "plugin_cache";

    public RestoredPluginLocation RestoreToCache(
        string configRoot,
        PluginRequirement requirement,
        PluginSourceSnapshot source,
        Action<string>? log,
        bool isLocal = false
    )
    {
        var pluginName = GetCacheSegment(source.Manifest.Name, "name");
        var pluginVersion = GetCacheSegment(source.Manifest.Version, "version");
        var pluginCacheRoot = GetCacheRoot(configRoot) / pluginName;
        pluginCacheRoot.CreateDirectory();

        var destinationRoot = pluginCacheRoot / pluginVersion;
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
                    log?.Invoke($"Using cached plugin '{requirement.DisplaySource}'.");
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
                    $"Refreshing invalid cached plugin '{requirement.DisplaySource}': {exception.Message}"
                );
            }
        }

        var stagingRoot = pluginCacheRoot / $"{pluginVersion}.staging.{Guid.NewGuid():N}";
        var backupRoot = pluginCacheRoot / $"{pluginVersion}.backup.{Guid.NewGuid():N}";
        try
        {
            log?.Invoke($"Restoring plugin '{requirement.DisplaySource}'.");
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

    private AbsolutePath GetCacheRoot(string configRoot) =>
        cacheRootOverride is null
            ? configRoot.AsLocalPath().ResolveToCurrentDirectory()
                / CacheDirectoryName
                / PluginCacheDirectoryName
            : cacheRootOverride.AsAbsolutePath();

    private static string GetCacheSegment(string? value, string propertyName)
    {
        var segment = value?.Trim();
        if (
            string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".."
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || segment.Contains('/')
            || segment.Contains('\\')
            || Path.GetFileName(segment) != segment
        )
        {
            throw new ConfigurationException(
                $"Plugin manifest '{propertyName}' must be a valid directory name."
            );
        }

        return segment;
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
