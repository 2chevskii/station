using Station.Core.Configuration.Models;
using TruePath;
using TruePath.SystemIo;

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
                new AbsolutePath(moduleRoot.FullPath) / $"{moduleRoot.Name}.dll"
            )
            .Where(static path => path.ExistsFile())
            .Select(static path => path.Value)
            .ToArray();
    }

    public string GetPluginsDirectory(string configRoot) =>
        treeDiscovery.Discover(configRoot).PluginsRoot?.FullPath
        ?? (new LocalPath(configRoot).ResolveToCurrentDirectory() / "plugins").Value;
}
