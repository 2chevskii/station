using Station.Core.Configuration.Discovery;
namespace Station.Core.Configuration.Roles;

public sealed record ResolvedRole(
    string ProviderName,
    string ShortName,
    string FullName,
    string RoleRoot,
    DiscoveredDirectory RoleDirectory
);
