namespace Station.Core.Configuration.Models;

internal sealed class PluginManifestFile
{
    public string? Name { get; init; }

    public string? Version { get; init; }

    public List<string>? Roles { get; init; }

    public List<string>? Modules { get; init; }
}
