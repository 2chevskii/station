namespace Station.Abstractions.Models;

public sealed class ModuleArtifactSet
{
    private readonly IReadOnlyDictionary<string, ManagedFile> filesByName;
    private readonly IReadOnlyDictionary<string, ManagedTemplate> templatesByExactName;
    private readonly IReadOnlyDictionary<
        string,
        IReadOnlyList<ManagedTemplate>
    > templatesByLogicalName;

    public ModuleArtifactSet(
        IReadOnlyDictionary<string, ManagedFile> filesByName,
        IReadOnlyDictionary<string, ManagedTemplate> templatesByExactName,
        IReadOnlyDictionary<string, IReadOnlyList<ManagedTemplate>> templatesByLogicalName
    )
    {
        this.filesByName = filesByName;
        this.templatesByExactName = templatesByExactName;
        this.templatesByLogicalName = templatesByLogicalName;
    }

    public ManagedFile GetRequiredFile(string name)
    {
        if (TryGetFile(name, out var file))
        {
            return file;
        }

        throw new ConfigurationException($"File '{name}' is not available in the module context.");
    }

    public bool TryGetFile(string name, out ManagedFile file) =>
        filesByName.TryGetValue(NormalizeName(name), out file!);

    public ManagedTemplate GetRequiredTemplate(string name)
    {
        if (TryGetTemplate(name, out var template, out var error))
        {
            return template;
        }

        throw new ConfigurationException(
            error ?? $"Template '{name}' is not available in the module context."
        );
    }

    public bool TryGetTemplate(string name, out ManagedTemplate template, out string? error)
    {
        var normalizedName = NormalizeName(name);
        if (HasTemplateExtension(normalizedName))
        {
            if (templatesByExactName.TryGetValue(normalizedName, out template!))
            {
                error = null;
                return true;
            }

            error = $"Template '{name}' is not available in the module context.";
            return false;
        }

        if (
            !templatesByLogicalName.TryGetValue(normalizedName, out var matches)
            || matches.Count == 0
        )
        {
            template = null!;
            error = $"Template '{name}' is not available in the module context.";
            return false;
        }

        if (matches.Count > 1)
        {
            template = null!;
            error =
                $"Template '{name}' is ambiguous. Use an explicit extension: {string.Join(", ", matches.Select(static item => item.Name))}.";
            return false;
        }

        template = matches[0];
        error = null;
        return true;
    }

    private static bool HasTemplateExtension(string name) =>
        name.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string name) => name.Replace('\\', '/').TrimStart('/');
}
