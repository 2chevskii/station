using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;

namespace Station.Modules.Builtin.Dotfile;

[Module( "copy", "Copies a file from the module artifact tree to the local machine." )]
public sealed class CopyModule : ConfigModule
{
    public CopyModule( ModuleContext context )
        : base( context )
    {
    }

    [Parameter( "file", Required = true )] public string FileName { get; set; } = string.Empty;

    [Parameter( Required = true )] public string Destination { get; set; } = string.Empty;

    [Parameter] public bool Backup { get; set; } = true;

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = Context.Artifacts.GetRequiredFile( FileName );
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
            var sourceFile = Context.Artifacts.GetRequiredFile( FileName );
            var destinationPath = HostFilePaths.ResolveDestination( Destination );
            var targetPath = HostFilePaths.ResolveTarget( destinationPath );
            var desiredBytes = sourceFile.ReadAllBytes();
            return await FileDeployment.ApplyAsync(
                destinationPath,
                targetPath,
                desiredBytes,
                Backup,
                Context,
                "File",
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
