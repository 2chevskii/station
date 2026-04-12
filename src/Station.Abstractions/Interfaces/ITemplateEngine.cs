namespace Station.Abstractions.Interfaces;

using Station.Abstractions.Models;

public interface ITemplateEngine
{
    string Render(string template, VariableSet variables, string? sourceName = null);
}
