using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Loading;
using Station.Plugins.Abstractions.Requirements;
using TruePath;

namespace Station.Core.Configuration.Plugins;

internal sealed class PluginRequirementParser
{
    private static readonly string[] ArchiveExtensions = [".zip", ".tar", ".tar.gz", ".tgz", ".7z"];

    public IReadOnlyList<PluginRequirement> Parse(IReadOnlyList<object?>? nodes, string sourceName)
    {
        var requirements = new List<PluginRequirement>();
        foreach (var node in nodes ?? [])
        {
            requirements.Add(
                node switch
                {
                    string shortForm => ParseShortForm(shortForm),
                    IDictionary<string, object?> mapping => ParseLongForm(mapping, sourceName),
                    _ => throw new ConfigurationException(
                        $"Plugins in '{sourceName}' must be declared as strings or mappings."
                    ),
                }
            );
        }

        return requirements;
    }

    private PluginRequirement ParseShortForm(string shortForm)
    {
        var trimmed = shortForm.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ConfigurationException("Plugin requirements must not be empty.");
        }

        if (trimmed.StartsWith("nuget://", StringComparison.OrdinalIgnoreCase))
        {
            var idAndVersion = trimmed["nuget://".Length..];
            var (id, version) = SplitPackageCoordinate(idAndVersion);
            return new PluginRequirement("nuget", trimmed, Id: id, Version: version);
        }

        if (trimmed.StartsWith("npm://", StringComparison.OrdinalIgnoreCase))
        {
            var idAndVersion = trimmed["npm://".Length..];
            var (id, version) = SplitPackageCoordinate(idAndVersion);
            return new PluginRequirement("npm", trimmed, Id: id, Version: version);
        }

        if (trimmed.StartsWith("git://", StringComparison.OrdinalIgnoreCase))
        {
            var (url, gitRef) = SplitGitReference(trimmed);
            return new PluginRequirement("git", trimmed, Url: url, Ref: gitRef);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            var normalized = absoluteUri.ToString();
            if (HasArchiveExtension(absoluteUri.AbsolutePath))
            {
                return new PluginRequirement("url", trimmed, Url: normalized);
            }

            if (
                normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                || UrlLooksLikeGitRepository(absoluteUri)
            )
            {
                var (url, gitRef) = SplitGitReference(normalized);
                return new PluginRequirement("git", trimmed, Url: url, Ref: gitRef);
            }

            throw new ConfigurationException(
                $"Remote plugin source '{trimmed}' requires either an archive extension or an explicit provider."
            );
        }

        return new PluginRequirement("local", trimmed, Name: trimmed);
    }

    private static PluginRequirement ParseLongForm(
        IDictionary<string, object?> mapping,
        string sourceName
    )
    {
        var providerName = mapping.TryGetValue("provider", out var providerValue)
            ? providerValue?.ToString()
            : null;

        var provider = string.IsNullOrWhiteSpace(providerName)
            ? InferLongFormProvider(mapping)
            : providerName.Trim().ToLowerInvariant();

        return provider switch
        {
            "local" => new PluginRequirement(
                provider,
                mapping.RequireString("name", sourceName),
                Name: mapping.RequireString("name", sourceName)
            ),
            "git" => new PluginRequirement(
                provider,
                mapping.RequireString("url", sourceName),
                Url: mapping.RequireString("url", sourceName),
                Ref: mapping.GetOptionalString("ref")
            ),
            "url" => new PluginRequirement(
                provider,
                mapping.RequireString("url", sourceName),
                Url: mapping.RequireString("url", sourceName)
            ),
            "nuget" => new PluginRequirement(
                provider,
                mapping.RequireString("id", sourceName),
                Id: mapping.RequireString("id", sourceName),
                Version: mapping.GetOptionalString("version"),
                Feed: mapping.GetOptionalString("feed")
            ),
            "npm" => new PluginRequirement(
                provider,
                mapping.RequireString("id", sourceName),
                Id: mapping.RequireString("id", sourceName),
                Version: mapping.GetOptionalString("version"),
                Registry: mapping.GetOptionalString("registry")
            ),
            _ => throw new ConfigurationException(
                $"Unsupported plugin provider '{providerName}' in '{sourceName}'."
            ),
        };
    }

    private static string InferLongFormProvider(IDictionary<string, object?> mapping)
    {
        if (mapping.ContainsKey("name"))
        {
            return "local";
        }

        throw new ConfigurationException(
            "Plugin object requirements without 'provider' currently require a 'name'."
        );
    }

    private static (string Id, string? Version) SplitPackageCoordinate(string value)
    {
        var separatorIndex = value.LastIndexOf('@');
        if (
            separatorIndex == 0 && value.StartsWith('@')
            || separatorIndex <= 0
            || value.StartsWith('@') && value.Count(static character => character == '@') == 1
        )
        {
            return (value, null);
        }

        var id = value[..separatorIndex];
        var version = value[(separatorIndex + 1)..];
        return (id, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    private static (string Url, string? Ref) SplitGitReference(string value)
    {
        var lastAt = value.LastIndexOf('@');
        if (lastAt <= 0)
        {
            return (value, null);
        }

        var lastSlash = value.LastIndexOf('/');
        var lastColon = value.LastIndexOf(':');
        var markerIndex = Math.Max(lastSlash, lastColon);
        if (lastAt <= markerIndex)
        {
            return (value, null);
        }

        var url = value[..lastAt];
        var gitRef = value[(lastAt + 1)..];
        return string.IsNullOrWhiteSpace(gitRef) ? (value, null) : (url, gitRef);
    }

    private static bool UrlLooksLikeGitRepository(Uri uri)
    {
        var lastSegment = uri.Segments.LastOrDefault()?.TrimEnd('/');
        return !string.IsNullOrWhiteSpace(lastSegment)
            && string.IsNullOrEmpty(lastSegment.AsLocalPath().GetExtensionWithDot());
    }

    private static bool HasArchiveExtension(string value) =>
        ArchiveExtensions.Any(extension =>
            value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
        );
}
