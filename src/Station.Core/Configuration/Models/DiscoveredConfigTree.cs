namespace Station.Core.Configuration.Models;

public sealed record DiscoveredConfigTree(
    string RootPath,
    DiscoveredDirectory RawRoot,
    DiscoveredFile? RootManifest,
    DiscoveredDirectory? RolesRoot,
    DiscoveredDirectory? VarsRoot,
    DiscoveredDirectory? FilesRoot,
    DiscoveredDirectory? TemplatesRoot,
    DiscoveredDirectory? PluginsRoot,
    DiscoveredDirectory? ModulesRoot,
    IReadOnlyList<DiscoveredDirectory> LocalRoleDirectories,
    IReadOnlyList<DiscoveredDirectory> LocalModuleDirectories
);
