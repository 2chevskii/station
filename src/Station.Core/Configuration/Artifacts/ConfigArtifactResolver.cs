using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Configuration.Artifacts;

internal sealed class ConfigArtifactResolver : IConfigArtifactResolver
{
    private readonly IReadOnlyList<IRoleDocumentLoader> roleLoaders;
    private readonly IReadOnlyList<IVariableDocumentLoader> variableLoaders;

    public ConfigArtifactResolver(
        IEnumerable<IRoleDocumentLoader> roleLoaders,
        IEnumerable<IVariableDocumentLoader> variableLoaders
    )
    {
        this.roleLoaders = roleLoaders.ToArray();
        this.variableLoaders = variableLoaders.ToArray();
    }

    public IReadOnlyList<ResolvedConfigArtifact> EnumerateRootVarArtifacts(
        DiscoveredConfigTree tree
    )
    {
        if (tree.VarsRoot is null)
        {
            return [];
        }

        var candidates = tree
            .VarsRoot.Files.Select(file => new
            {
                File = file,
                Loader = variableLoaders.FirstOrDefault(loader =>
                    loader.Supports(ConfigArtifactKind.RootVarsEntry, file.Name)
                ),
            })
            .Where(static item => item.Loader is not null)
            .OrderBy(static item => item.File.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicateBaseName = candidates
            .GroupBy(
                static item => item.File.Name.AsLocalPath().GetFilenameWithoutExtension(),
                StringComparer.OrdinalIgnoreCase
            )
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateBaseName is not null)
        {
            var names = duplicateBaseName
                .Select(static item => item.File.Name)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            throw new ConfigurationException(
                $"Vars files '{names[0]}' and '{names[1]}' are ambiguous because they share the same basename."
            );
        }

        return candidates
            .Select(item => new ResolvedConfigArtifact(
                ConfigArtifactKind.RootVarsEntry,
                item.File.FullPath,
                item.File.RelativePath,
                tree.RootPath,
                null,
                item.Loader!.Id
            ))
            .ToArray();
    }

    public ResolvedConfigArtifact? ResolveRoleDefaultsArtifact(
        DiscoveredDirectory roleDirectory,
        string roleFullName
    )
    {
        var candidates = ResolveCandidates(
            roleDirectory,
            ConfigArtifactKind.RoleDefaults,
            variableLoaders
                .Select(static loader =>
                    (loader.Id, Supports: (Func<ConfigArtifactKind, string, bool>)loader.Supports)
                )
                .ToArray(),
            roleFullName
        );

        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new ConfigurationException(
                $"Role '{roleFullName}' contains both 'defaults.yaml' and 'defaults.js'."
            ),
        };
    }

    public ResolvedConfigArtifact ResolveRoleTaskArtifact(
        DiscoveredDirectory roleDirectory,
        string roleFullName
    )
    {
        var candidates = ResolveCandidates(
            roleDirectory,
            ConfigArtifactKind.RoleTasks,
            roleLoaders
                .Select(static loader =>
                    (loader.Id, Supports: (Func<ConfigArtifactKind, string, bool>)loader.Supports)
                )
                .ToArray(),
            roleFullName
        );

        return candidates.Count switch
        {
            0 => throw new ConfigurationException(
                $"Role '{roleFullName}' is missing 'role.yaml' or 'role.js'."
            ),
            1 => candidates[0],
            _ => throw new ConfigurationException(
                $"Role '{roleFullName}' contains both 'role.yaml' and 'role.js'."
            ),
        };
    }

    private static List<ResolvedConfigArtifact> ResolveCandidates(
        DiscoveredDirectory roleDirectory,
        ConfigArtifactKind kind,
        IReadOnlyList<(string Id, Func<ConfigArtifactKind, string, bool> Supports)> loaders,
        string roleFullName
    )
    {
        var results = new List<ResolvedConfigArtifact>();
        foreach (var file in roleDirectory.Files)
        {
            var loader = loaders.FirstOrDefault(candidate => candidate.Supports(kind, file.Name));
            if (loader.Id is null)
            {
                continue;
            }

            results.Add(
                new ResolvedConfigArtifact(
                    kind,
                    file.FullPath,
                    file.RelativePath,
                    roleDirectory.FullPath,
                    roleFullName,
                    loader.Id
                )
            );
        }

        return results;
    }
}
