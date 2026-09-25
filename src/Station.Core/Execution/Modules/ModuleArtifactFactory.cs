using Station.Abstractions.FileSystem;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Execution.Modules;

internal static class ModuleArtifactFactory
{
    public static ModuleArtifactSet Create(
        string configRoot,
        string? roleRoot,
        VariableSet variables,
        ITemplateEngine templateRenderer
    )
    {
        var files = new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase);
        var exactTemplates = new Dictionary<string, ManagedTemplate>(
            StringComparer.OrdinalIgnoreCase
        );
        var logicalTemplates = new Dictionary<string, List<ManagedTemplate>>(
            StringComparer.OrdinalIgnoreCase
        );

        AddScope(configRoot);
        if (!string.IsNullOrWhiteSpace(roleRoot))
        {
            AddScope(roleRoot!);
        }

        return new ModuleArtifactSet(
            files,
            exactTemplates,
            logicalTemplates.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<ManagedTemplate>)pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase
            )
        );

        void AddScope(string root)
        {
            var scopeRoot = root.AsAbsolutePath();
            var filesRoot = scopeRoot / "files";
            if (filesRoot.ExistsDirectory())
            {
                foreach (var path in filesRoot.GetFiles("*", SearchOption.AllDirectories))
                {
                    var filePath = path.AsAbsolutePath();
                    var logicalName = Normalize(filePath.RelativeTo(filesRoot).Value);
                    files[logicalName] = new ManagedFile(logicalName, filePath.Value);
                }
            }

            var templatesRoot = scopeRoot / "templates";
            if (!templatesRoot.ExistsDirectory())
            {
                return;
            }

            var templatesInScope = templatesRoot
                .GetFiles("*", SearchOption.AllDirectories)
                .Select(static path => path.AsAbsolutePath())
                .Where(static path =>
                    path.Value.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase)
                    || path.Value.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase)
                )
                .Select(path => new
                {
                    SourcePath = path.Value,
                    ExactName = Normalize(path.RelativeTo(templatesRoot).Value),
                    LogicalName = Normalize(
                        RemoveTemplateExtension(path.RelativeTo(templatesRoot).Value)
                    ),
                })
                .ToArray();

            foreach (
                var logicalName in templatesInScope
                    .Select(static item => item.LogicalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                if (logicalTemplates.TryGetValue(logicalName, out var previousMatches))
                {
                    foreach (var previous in previousMatches)
                    {
                        exactTemplates.Remove(previous.Name);
                    }
                }

                logicalTemplates.Remove(logicalName);
            }

            foreach (var template in templatesInScope)
            {
                var managedTemplate = new ManagedTemplate(
                    template.ExactName,
                    template.SourcePath,
                    variables,
                    templateRenderer
                );
                exactTemplates[template.ExactName] = managedTemplate;
                if (!logicalTemplates.TryGetValue(template.LogicalName, out var matches))
                {
                    matches = [];
                    logicalTemplates[template.LogicalName] = matches;
                }

                matches.Add(managedTemplate);
            }
        }
    }

    private static string RemoveTemplateExtension(string path)
    {
        if (path.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase))
        {
            return path[..^".scriban".Length];
        }

        if (path.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase))
        {
            return path[..^".sbn".Length];
        }

        return path;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
