using Station.Core.Configuration.Models;

namespace Station.Core.Configuration.Services;

internal sealed class LocalConfigurationDiscovery
{
    private readonly ConfigTreeDiscovery treeDiscovery = new();

    public IReadOnlyList<ResolvedRole> DiscoverLocalRoles(string configRoot)
    {
        var tree = treeDiscovery.Discover(configRoot);
        return tree
            .LocalRoleDirectories.Select(roleDirectory => new ResolvedRole(
                "station.local",
                roleDirectory.Name,
                $"station.local.{roleDirectory.Name}",
                roleDirectory.FullPath,
                roleDirectory
            ))
            .ToArray();
    }

    public IReadOnlyList<string> DiscoverLocalModulePaths(string configRoot)
    {
        var tree = treeDiscovery.Discover(configRoot);
        return tree
            .LocalModuleDirectories.Select(moduleRoot =>
                Path.Combine(moduleRoot.FullPath, $"{moduleRoot.Name}.dll")
            )
            .Where(File.Exists)
            .ToArray();
    }

    public string GetPluginsDirectory(string configRoot) =>
        treeDiscovery.Discover(configRoot).PluginsRoot?.FullPath
        ?? Path.Combine(Path.GetFullPath(configRoot), "plugins");
}
