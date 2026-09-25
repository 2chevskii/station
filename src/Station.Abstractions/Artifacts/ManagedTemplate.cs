using Station.Abstractions.Configuration;
using Station.Abstractions.FileSystem;

using TruePath.SystemIo;

namespace Station.Abstractions.Artifacts;

public sealed class ManagedTemplate
{
    private readonly ITemplateEngine templateEngine;
    private readonly VariableSet variables;
    private string? rawText;
    private string? renderedText;

    public ManagedTemplate(
        string name,
        string sourcePath,
        VariableSet variables,
        ITemplateEngine templateEngine
    )
    {
        Name = name;
        SourcePath = sourcePath;
        this.variables = variables;
        this.templateEngine = templateEngine;
    }

    public string Name { get; }

    public string SourcePath { get; }

    public string GetRawText()
    {
        rawText ??= SourcePath.AsAbsolutePath().ReadAllText();
        return rawText;
    }

    public string GetRenderedText()
    {
        renderedText ??= templateEngine.Render( GetRawText(), variables, SourcePath );
        return renderedText;
    }

    public string Render( VariableSet additionalVariables )
    {
        var overlay = variables.CreateOverlay( additionalVariables.Root );
        return templateEngine.Render( GetRawText(), overlay, SourcePath );
    }
}