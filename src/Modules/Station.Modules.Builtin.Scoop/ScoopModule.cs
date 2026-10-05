using System.Text.RegularExpressions;

using Station.Abstractions.Configuration;
using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Abstractions.Processes;

namespace Station.Modules.Builtin.Scoop;

[Module( "scoop", "Installs packages through Scoop." )]
public sealed class ScoopModule : ConfigModule
{
    private readonly IProcessRunner processRunner;

    public ScoopModule( ModuleContext context, IProcessRunner processRunner )
        : base( context )
    {
        this.processRunner = processRunner;
    }

    [Parameter( "name", Required = true )] public string PackageName { get; set; } = string.Empty;

    [Parameter] public string State { get; set; } = "present";

    [Parameter( "global" )] public bool IsGlobal { get; set; }

    [Parameter] public string Command { get; set; } = "scoop";

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

        var privilege = IsGlobal ? PrivilegeRequirement.Administrator : PrivilegeRequirement.User;

        return ValueTask.FromResult( ModuleInspectionResult.Success( privilege ) );
    }

    public override async ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameterError = ValidateParameters();
        if ( parameterError is not null )
            return ModuleResult.Failure( parameterError );

        var exportResult = await processRunner.RunAsync(
            new ProcessCommand( Command, ["export"] ),
            cancellationToken
        );
        if ( exportResult.ExitCode != 0 )
            return ModuleResult.Failure(
                $"Could not query Scoop packages: {SelectProcessError( exportResult )}"
            );

        var appName = PackageName[(PackageName.LastIndexOf( '/' ) + 1)..];
        bool installed;
        try
        {
            installed = ScoopExportReader.IsInstalled(
                exportResult.StandardOutput,
                appName,
                IsGlobal
            );
        }
        catch ( ConfigurationException exception )
        {
            return ModuleResult.Failure( exception.Message );
        }

        var shouldExist = State.Trim().Equals( "present", StringComparison.OrdinalIgnoreCase );
        var changed = shouldExist != installed;

        if ( Context.Mode == ExecutionMode.Check )
            return changed
                ? ModuleResult.ChangedResult( $"scoop package '{PackageName}' would change." )
                : ModuleResult.NoChange(
                    $"scoop package '{PackageName}' is already in the desired state."
                );

        if ( !changed )
            return ModuleResult.NoChange(
                $"scoop package '{PackageName}' is already in the desired state."
            );

        var arguments = new List<string> { shouldExist ? "install" : "uninstall" };
        if ( IsGlobal )
            arguments.Add( "--global" );

        arguments.Add( shouldExist ? PackageName : appName );

        var result = await processRunner.RunAsync(
            new ProcessCommand( Command, arguments ),
            cancellationToken
        );
        if ( result.ExitCode != 0 )
            return ModuleResult.Failure( SelectProcessError( result ) );

        return ModuleResult.ChangedResult( $"scoop package '{PackageName}' was updated." );
    }

    private string? ValidateParameters()
    {
        if ( State?.Trim().ToLowerInvariant() is not ("present" or "absent") )
            return "Parameter 'state' must be 'present' or 'absent'.";

        if ( string.IsNullOrWhiteSpace( PackageName ) || !Regex.IsMatch(
            PackageName,
            @"\A(?:[A-Za-z0-9][A-Za-z0-9_.-]*/)?[A-Za-z0-9][A-Za-z0-9_.-]*\z",
            RegexOptions.CultureInvariant
        ) )
            return "Parameter 'name' must be a Scoop package name or bucket/name.";

        return null;
    }

    private static string SelectProcessError( ProcessResult result ) =>
        !string.IsNullOrWhiteSpace( result.StandardError )
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace( result.StandardOutput )
                ? result.StandardOutput.Trim()
                : "scoop failed.";
}
