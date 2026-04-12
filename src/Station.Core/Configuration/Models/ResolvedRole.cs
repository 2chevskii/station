namespace Station.Core.Configuration.Models;

public sealed record ResolvedRole(
    string ProviderName,
    string ShortName,
    string FullName,
    string RoleRoot);
