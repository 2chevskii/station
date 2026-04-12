using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Fluid;

namespace ConfigTool.Core;

public sealed class FluidTemplateRenderer : ITemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\$\{(?<name>[A-Za-z0-9_.-]+)\}", RegexOptions.Compiled);
    private readonly FluidParser parser = new();
    private readonly ConcurrentDictionary<string, IFluidTemplate> cache = new(StringComparer.Ordinal);

    public string Render(string template, IReadOnlyDictionary<string, string> variables, string? sourceName = null)
    {
        var normalizedTemplate = PlaceholderRegex.Replace(template, static match => $"{{{{ {match.Groups["name"].Value} }}}}");
        var compiledTemplate = cache.GetOrAdd(normalizedTemplate, ParseTemplate);
        var context = new TemplateContext(new TemplateOptions());

        foreach (var (key, value) in variables)
        {
            context.SetValue(key, value);
        }

        return compiledTemplate.Render(context);

        IFluidTemplate ParseTemplate(string templateText)
        {
            if (!parser.TryParse(templateText, out var parsed, out var error))
            {
                throw new ConfigurationException(
                    $"Failed to parse template{FormatSourceName(sourceName)}: {error}");
            }

            return parsed;
        }
    }

    private static string FormatSourceName(string? sourceName) =>
        string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $" '{sourceName}'";
}
