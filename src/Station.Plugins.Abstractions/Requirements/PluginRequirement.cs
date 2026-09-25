namespace Station.Plugins.Abstractions.Requirements;

public sealed record PluginRequirement(
    string Provider,
    string DisplaySource,
    string? Name = null,
    string? Url = null,
    string? Ref = null,
    string? Id = null,
    string? Version = null,
    string? Feed = null,
    string? Registry = null
);