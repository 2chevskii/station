namespace Station.Core.Configuration.Models;

public sealed record PluginDeclaration(
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
