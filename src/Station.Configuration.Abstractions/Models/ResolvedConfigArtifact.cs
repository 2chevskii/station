namespace Station.Configuration.Abstractions.Models;

public sealed record ResolvedConfigArtifact(
    ConfigArtifactKind Kind,
    string FullPath,
    string DisplayName,
    string OwningRoot,
    string? RoleName,
    string LoaderId
);
