using Station.Core.Configuration.Models;

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
        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
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
            fullRoot,
            rawRoot,
            rootManifest,
            rolesRoot,
            varsRoot,
            filesRoot,
            templatesRoot,
            pluginsRoot,
            modulesRoot,
            rolesRoot?.Directories ?? [],
            modulesRoot?.Directories ?? []);
    }

    private static DiscoveredDirectory DiscoverDirectory(string fullPath, string relativePath)
    {
        var directories = Directory
            .GetDirectories(fullPath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => DiscoverDirectory(path, CombineRelative(relativePath, Path.GetFileName(path))))
            .ToArray();

        var files = Directory
            .GetFiles(fullPath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new DiscoveredFile(
                path,
                Path.GetFileName(path),
                CombineRelative(relativePath, Path.GetFileName(path))))
            .ToArray();

        return new DiscoveredDirectory(
            fullPath,
            Path.GetFileName(fullPath),
            relativePath,
            directories,
            files);
    }

    private static string CombineRelative(string parent, string name) =>
        string.IsNullOrWhiteSpace(parent) ? name : $"{parent}/{name}";
}
