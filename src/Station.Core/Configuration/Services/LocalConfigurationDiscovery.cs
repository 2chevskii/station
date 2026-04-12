using Station.Core.Configuration.Models;

namespace Station.Core.Configuration.Services;

internal sealed class LocalConfigurationDiscovery
{
    private const string ModulesDirectoryName = "modules";
    private const string PluginsDirectoryName = "plugins";
    private const string RolesDirectoryName = "roles";

    public IReadOnlyList<ResolvedRole> DiscoverLocalRoles(string configRoot)
    {
        var rolesRoot = Path.Combine(Path.GetFullPath(configRoot), RolesDirectoryName);
        if (!Directory.Exists(rolesRoot))
        {
            return [];
        }

        return Directory
            .GetDirectories(rolesRoot)
            .Select(roleRoot => new ResolvedRole(
                "station.local",
                Path.GetFileName(roleRoot),
                $"station.local.{Path.GetFileName(roleRoot)}",
                roleRoot
            ))
            .ToArray();
    }

    public IReadOnlyList<string> DiscoverLocalModulePaths(string configRoot)
    {
        var modulesRoot = Path.Combine(Path.GetFullPath(configRoot), ModulesDirectoryName);
        if (!Directory.Exists(modulesRoot))
        {
            return [];
        }

        return Directory
            .GetDirectories(modulesRoot)
            .Select(moduleRoot => Path.Combine(moduleRoot, $"{Path.GetFileName(moduleRoot)}.dll"))
            .Where(File.Exists)
            .ToArray();
    }

    public string GetPluginsDirectory(string configRoot) =>
        Path.Combine(Path.GetFullPath(configRoot), PluginsDirectoryName);
}
