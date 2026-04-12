using Station.Abstractions.Models;
using Station.Core.Configuration;

namespace Station.Core.Execution;

internal sealed class RoleCatalog
{
    private readonly IReadOnlyDictionary<string, ResolvedRole> rolesByFullName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ResolvedRole>> rolesByShortName;

    private RoleCatalog(
        IReadOnlyDictionary<string, ResolvedRole> rolesByFullName,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedRole>> rolesByShortName)
    {
        this.rolesByFullName = rolesByFullName;
        this.rolesByShortName = rolesByShortName;
    }

    public static RoleCatalog Create(
        IEnumerable<ResolvedRole> localRoles,
        IEnumerable<ResolvedPluginManifest> plugins)
    {
        var roles = new List<ResolvedRole>(localRoles);

        foreach (var plugin in plugins)
        {
            roles.AddRange(plugin.Roles.Select(shortName => new ResolvedRole(
                plugin.Name,
                shortName,
                $"{plugin.Name}.{shortName}",
                Path.Combine(plugin.PluginRoot, "roles", shortName))));
        }

        return new RoleCatalog(
            roles.ToDictionary(role => role.FullName, StringComparer.OrdinalIgnoreCase),
            roles.GroupBy(role => role.ShortName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ResolvedRole>)group.ToArray(),
                    StringComparer.OrdinalIgnoreCase));
    }

    public ResolvedRole Resolve(string name)
    {
        if (rolesByFullName.TryGetValue(name, out var fullNameMatch))
        {
            return fullNameMatch;
        }

        if (!rolesByShortName.TryGetValue(name, out var shortNameMatches))
        {
            throw new ConfigurationException($"Role '{name}' is not registered.");
        }

        if (shortNameMatches.Count > 1)
        {
            throw new ConfigurationException(
                $"Role '{name}' is ambiguous. Use one of: {string.Join(", ", shortNameMatches.Select(static role => role.FullName))}.");
        }

        return shortNameMatches[0];
    }
}
