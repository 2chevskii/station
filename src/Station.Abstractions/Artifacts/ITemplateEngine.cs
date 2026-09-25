using Station.Abstractions.Configuration;

namespace Station.Abstractions.Artifacts;

public interface ITemplateEngine
{
    string Render( string template, VariableSet variables, string? sourceName = null );
}