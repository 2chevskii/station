namespace Station.Plugins.Abstractions;

public sealed class PluginManifest
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public List<string>? Roles { get; init; }
    public List<string>? Modules { get; init; }
}
