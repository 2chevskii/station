namespace Station.Core.Configuration.Models;

internal sealed record ResolvedConfigArtifact(
    ConfigArtifactKind Kind,
    string FullPath,
    string DisplayName,
    string OwningRoot,
    string? RoleName,
    string LoaderId);
