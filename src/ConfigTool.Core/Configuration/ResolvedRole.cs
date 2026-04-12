namespace ConfigTool.Core;

public sealed record ResolvedRole(
    string ProviderName,
    string ShortName,
    string FullName,
    string RoleRoot);
