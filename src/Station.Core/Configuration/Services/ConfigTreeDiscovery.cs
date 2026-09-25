using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Models;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Configuration.Services;

internal sealed class ConfigTreeDiscovery
{
    private const string FilesDirectoryName = "files";
    private const string ModulesDirectoryName = "modules";
    private const string PluginsDirectoryName = "plugins";
    private const string RolesDirectoryName = "roles";
    private const string RootManifestFileName = "station.yaml";
    private const string TemplatesDirectoryName = "templates";
    private const string VarsDirectoryName = "vars";

    public DiscoveredConfigTree Discover(string rootPath)
    {
        var fullRoot = rootPath.AsLocalPath().ResolveToCurrentDirectory();
        if (!fullRoot.ExistsDirectory())
        {
            throw new ConfigurationException($"Configuration root '{fullRoot}' does not exist.");
        }

        var rawRoot = DiscoverDirectory(fullRoot, string.Empty);
        rawRoot.TryGetFile(RootManifestFileName, out var rootManifest);
        rawRoot.TryGetDirectory(RolesDirectoryName, out var rolesRoot);
        rawRoot.TryGetDirectory(VarsDirectoryName, out var varsRoot);
        rawRoot.TryGetDirectory(FilesDirectoryName, out var filesRoot);
        rawRoot.TryGetDirectory(TemplatesDirectoryName, out var templatesRoot);
        rawRoot.TryGetDirectory(PluginsDirectoryName, out var pluginsRoot);
        rawRoot.TryGetDirectory(ModulesDirectoryName, out var modulesRoot);

        return new DiscoveredConfigTree(
            fullRoot.Value,
            rawRoot,
            rootManifest,
            rolesRoot,
            varsRoot,
            filesRoot,
            templatesRoot,
            pluginsRoot,
            modulesRoot,
            rolesRoot?.Directories ?? [],
            modulesRoot?.Directories ?? []
        );
    }

    private static DiscoveredDirectory DiscoverDirectory(AbsolutePath fullPath, string relativePath)
    {
        var directories = fullPath
            .GetDirectories()
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(static path => path.AsAbsolutePath())
            .Select(path =>
                DiscoverDirectory(path, CombineRelative(relativePath, path.FileName))
            )
            .ToArray();

        var files = fullPath
            .GetFiles()
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(static path => path.AsAbsolutePath())
            .Select(path => new DiscoveredFile(
                path.Value,
                path.FileName,
                CombineRelative(relativePath, path.FileName)
            ))
            .ToArray();

        return new DiscoveredDirectory(
            fullPath.Value,
            fullPath.FileName,
            relativePath,
            directories,
            files
        );
    }

    private static string CombineRelative(string parent, string name) =>
        string.IsNullOrWhiteSpace(parent) ? name : $"{parent}/{name}";
}
