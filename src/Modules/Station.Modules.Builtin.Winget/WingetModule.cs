using Station.Abstractions.Configuration;
using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Abstractions.Processes;

namespace Station.Modules.Builtin.Winget;

[Module( "winget", "Installs packages through winget." )]
public sealed class WingetModule : ConfigModule
{
    private const int NoApplicationsFound = unchecked((int)0x8A150014);
    private readonly IProcessRunner processRunner;

    public WingetModule( ModuleContext context, IProcessRunner processRunner )
        : base( context )
    {
        this.processRunner = processRunner;
    }

    [Parameter( "id", Required = true )] public string PackageId { get; set; } = string.Empty;

    [Parameter] public string State { get; set; } = "present";

    [Parameter] public string? Version { get; set; }

    [Parameter] public string? Scope { get; set; }

    [Parameter] public string Command { get; set; } = "winget";

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameterError = ValidateParameters();
        if ( parameterError is not null )
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure( new ModuleDiagnostic( parameterError ) )
            );

        if ( !processRunner.CommandExists( Command ) )
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure(
                    new ModuleDiagnostic( $"'{Command}' is not available on PATH." )
                )
            );

        var privilege = string.Equals( Scope?.Trim(), "machine", StringComparison.OrdinalIgnoreCase )
            ? PrivilegeRequirement.Administrator
            : PrivilegeRequirement.User;

        return ValueTask.FromResult( ModuleInspectionResult.Success( privilege ) );
    }

    public override async ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameterError = ValidateParameters();
        if ( parameterError is not null )
            return ModuleResult.Failure( parameterError );

        (bool IsInstalled, string? Version) installed;
        try
        {
            installed = await GetInstalledStateAsync( PackageId, cancellationToken );
        }
        catch ( ConfigurationException exception )
        {
            return ModuleResult.Failure( exception.Message );
        }

        var shouldExist = State.Trim().Equals( "present", StringComparison.OrdinalIgnoreCase );
        var needsVersionChange =
            shouldExist
            && !string.IsNullOrWhiteSpace( Version )
            && !string.Equals( installed.Version, Version, StringComparison.Ordinal );

        var changed = shouldExist != installed.IsInstalled || needsVersionChange;
        if ( Context.Mode == ExecutionMode.Check )
            return changed
                ? ModuleResult.ChangedResult( $"winget package '{PackageId}' would change." )
                : ModuleResult.NoChange(
                    $"winget package '{PackageId}' is already in the desired state."
                );

        if ( !changed )
            return ModuleResult.NoChange(
                $"winget package '{PackageId}' is already in the desired state."
            );

        var arguments =
            shouldExist
                ? CreateInstallArguments( PackageId, Version )
                : CreateUninstallArguments( PackageId );
        var result = await processRunner.RunAsync(
            new ProcessCommand( Command, arguments ),
            cancellationToken
        );
        if ( result.ExitCode != 0 )
            return ModuleResult.Failure( SelectProcessError( result ) );

        return ModuleResult.ChangedResult( $"winget package '{PackageId}' was updated." );
    }

    private async Task<(bool IsInstalled, string? Version)> GetInstalledStateAsync(
        string packageId,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "list",
            "--id",
            packageId,
            "--exact",
            "--disable-interactivity",
            "--accept-source-agreements",
        };
        AddScope( arguments );
        var result = await processRunner.RunAsync(
            new ProcessCommand( Command, arguments ),
            cancellationToken
        );

        if ( result.ExitCode == NoApplicationsFound )
            return (false, null);

        if ( result.ExitCode != 0 )
            throw new ConfigurationException(
                $"Could not query winget package '{packageId}': {SelectProcessError( result )}"
            );

        return (true, WingetListReader.ReadInstalledVersion( result.StandardOutput, packageId ));
    }

    private string? ValidateParameters()
    {
        if ( State.Trim().ToLowerInvariant() is not ("present" or "absent") )
            return "Parameter 'state' must be 'present' or 'absent'.";

        if ( string.IsNullOrWhiteSpace( PackageId ) )
            return "Parameter 'id' must be a non-empty package identifier.";

        if ( Scope is not null && Scope.Trim().ToLowerInvariant() is not ("user" or "machine") )
            return "Parameter 'scope' must be 'user' or 'machine'.";

        return null;
    }

    private IReadOnlyList<string> CreateInstallArguments(
        string id,
        string? version
    )
    {
        var arguments = new List<string>
        {
            "install",
            "--id",
            id,
            "--exact",
            "--disable-interactivity",
            "--accept-source-agreements",
            "--accept-package-agreements",
        };

        if ( !string.IsNullOrWhiteSpace( version ) )
        {
            arguments.Add( "--version" );
            arguments.Add( version );
        }

        AddScope( arguments );
        return arguments;
    }

    private IReadOnlyList<string> CreateUninstallArguments( string id )
    {
        var arguments = new List<string>
        {
            "uninstall", "--id", id, "--exact", "--disable-interactivity",
        };
        AddScope( arguments );
        return arguments;
    }

    private void AddScope( List<string> arguments )
    {
        if ( Scope is null )
            return;

        arguments.Add( "--scope" );
        arguments.Add( Scope.Trim().ToLowerInvariant() );
    }

    private static string SelectProcessError( ProcessResult result ) =>
        !string.IsNullOrWhiteSpace( result.StandardError )
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace( result.StandardOutput )
                ? result.StandardOutput.Trim()
                : "winget failed.";
}
