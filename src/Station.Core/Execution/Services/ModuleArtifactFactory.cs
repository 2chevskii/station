using Station.Abstractions.Interfaces;

namespace Station.Core.Execution.Services;

internal static class ModuleArtifactFactory
{
    public static ModuleArtifactSet Create(
        string configRoot,
        string? roleRoot,
        VariableSet variables,
        ITemplateEngine templateRenderer)
    {
        var files = new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase);
        var exactTemplates = new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase);
        var logicalTemplates = new Dictionary<string, List<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase);

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
            var filesRoot = Path.Combine(root, "files");
            if (Directory.Exists(filesRoot))
            {
                foreach (var filePath in Directory.GetFiles(filesRoot, "*", SearchOption.AllDirectories))
                {
                    var logicalName = Normalize(Path.GetRelativePath(filesRoot, filePath));
                    files[logicalName] = new ManagedFile(logicalName, filePath);
                }
            }

            var templatesRoot = Path.Combine(root, "templates");
            if (!Directory.Exists(templatesRoot))
            {
                return;
            }

            var templatesInScope = Directory
                .GetFiles(templatesRoot, "*", SearchOption.AllDirectories)
                .Where(static path =>
                    path.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase)
                )
                .Select(path => new
                {
                    SourcePath = path,
                    ExactName = Normalize(Path.GetRelativePath(templatesRoot, path)),
                    LogicalName = Normalize(RemoveTemplateExtension(Path.GetRelativePath(templatesRoot, path))),
                })
                .ToArray();

            foreach (var logicalName in templatesInScope.Select(static item => item.LogicalName).Distinct(StringComparer.OrdinalIgnoreCase))
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

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
