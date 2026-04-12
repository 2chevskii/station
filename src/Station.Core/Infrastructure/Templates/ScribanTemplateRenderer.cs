using System.Collections.Concurrent;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Station.Abstractions.Models;

namespace Station.Core.Infrastructure.Templates;

internal sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly LexerOptions lexerOptions = new LexerOptions { KeepTrivia = true };
    private readonly ConcurrentDictionary<string, Template> cache = new ConcurrentDictionary<
        string,
        Template
    >(StringComparer.Ordinal);

    public string Render(
        string template,
        IReadOnlyDictionary<string, string> variables,
        string? sourceName = null
    )
    {
        var compiledTemplate = cache.GetOrAdd(template, ParseTemplate);
        var context = new TemplateContext(ScriptObject.From(variables));

        return compiledTemplate.Render(context);

        Template ParseTemplate(string templateText)
        {
            var parsedTemplate = Template.Parse(
                templateText,
                sourceName,
                lexerOptions: lexerOptions
            );

            if (!parsedTemplate.HasErrors)
                return parsedTemplate;

            var formattedErrors = string.Join(
                " ,",
                parsedTemplate
                    .Messages.Where(x => x.Type == ParserMessageType.Error)
                    .Select(x => x.Message)
            );

            throw new ConfigurationException(
                $"Failed to parse template{FormatSourceName(sourceName)}: {formattedErrors}"
            );
        }
    }

    private static string FormatSourceName(string? sourceName) =>
        string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $" '{sourceName}'";
}
