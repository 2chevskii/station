using Station.Abstractions.Interfaces;
using Station.Abstractions.Models;

namespace Station.Testing;

public sealed class TestTemplateEngine : ITemplateEngine
{
    public List<(string Template, string? SourceName)> Calls { get; } = [];

    public string Render(string template, VariableSet variables, string? sourceName = null)
    {
        Calls.Add((template, sourceName));

        var rendered = template;
        foreach (var (path, value) in variables.EnumeratePaths())
        {
            rendered = rendered.Replace(
                "{{ " + path + " }}",
                value switch
                {
                    ConfigScalar scalar => Convert.ToString(scalar.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    _ => variables.Get<string>(path) ?? string.Empty,
                },
                StringComparison.Ordinal);
        }

        return rendered;
    }
}
