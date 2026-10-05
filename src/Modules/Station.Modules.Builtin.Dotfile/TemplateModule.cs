using System.Text;

using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;

namespace Station.Modules.Builtin.Dotfile;

[Module( "template", "Renders a template from the module artifact tree to the local machine." )]
public sealed class TemplateModule : ConfigModule
{
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding( false );

    public TemplateModule( ModuleContext context )
        : base( context )
    {
    }

    [Parameter( "template", Required = true )]
    public string TemplateName { get; set; } = string.Empty;

    [Parameter( Required = true )] public string Destination { get; set; } = string.Empty;

    [Parameter] public bool Backup { get; set; } = true;

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = Context.Artifacts.GetRequiredTemplate( TemplateName );
            var privilege = HostFilePaths.RequiresAdmin( Destination )
                ? PrivilegeRequirement.Administrator
                : PrivilegeRequirement.User;
            return ValueTask.FromResult( ModuleInspectionResult.Success( privilege ) );
        }
        catch ( Exception exception ) when (
            exception is ConfigurationException or IOException or UnauthorizedAccessException
        )
        {
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure( new ModuleDiagnostic( exception.Message ) )
            );
        }
    }

    public override async ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var template = Context.Artifacts.GetRequiredTemplate( TemplateName );
            var destinationPath = HostFilePaths.ResolveDestination( Destination );
            var targetPath = HostFilePaths.ResolveTarget( destinationPath );
            var desiredBytes = Utf8WithoutBom.GetBytes( template.GetRenderedText() );
            return await FileDeployment.ApplyAsync(
                destinationPath,
                targetPath,
                desiredBytes,
                Backup,
                Context,
                "Template",
                cancellationToken
            );
        }
        catch ( Exception exception ) when (
            exception is ConfigurationException or IOException or UnauthorizedAccessException
        )
        {
            return ModuleResult.Failure( exception.Message );
        }
    }
}
