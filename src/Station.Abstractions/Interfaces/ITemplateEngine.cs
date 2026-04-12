namespace Station.Abstractions.Interfaces;

using Models;

public interface ITemplateEngine
{
    string Render(string template, VariableSet variables, string? sourceName = null);
}
