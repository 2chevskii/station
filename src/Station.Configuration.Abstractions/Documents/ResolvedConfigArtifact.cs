namespace Station.Configuration.Abstractions.Documents;

public sealed record ResolvedConfigArtifact(
    ConfigArtifactKind Kind,
    string FullPath,
    string DisplayName,
    string OwningRoot,
    string? RoleName,
    string LoaderId
);