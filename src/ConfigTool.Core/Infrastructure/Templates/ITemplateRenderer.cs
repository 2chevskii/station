namespace ConfigTool.Core;

public interface ITemplateRenderer
{
    string Render(string template, IReadOnlyDictionary<string, string> variables, string? sourceName = null);
}
