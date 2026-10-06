using System.Text;
using System.Text.RegularExpressions;

using Station.Abstractions.Configuration;
using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Abstractions.Processes;

namespace Station.Modules.Builtin.Apt;

[Module( "apt", "Installs packages through apt locally or inside a WSL distribution." )]
public sealed class AptModule : ConfigModule
{
    private readonly IProcessRunner processRunner;

    public AptModule( ModuleContext context, IProcessRunner processRunner )
        : base( context )
    {
        this.processRunner = processRunner;
    }

    [Parameter( "name", Required = true )] public string PackageName { get; set; } = string.Empty;

    [Parameter] public string State { get; set; } = "present";

    [Parameter] public string? Version { get; set; }

    [Parameter] public string? Distribution { get; set; }

    [Parameter] public bool Sudo { get; set; } = true;

    [Parameter] public string WslCommand { get; set; } = "wsl";

    [Parameter] public string AptGetCommand { get; set; } = "apt-get";

    public override async ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameterError = ValidateParameters();
        if ( parameterError is not null )
            return ModuleInspectionResult.Failure( new ModuleDiagnostic( parameterError ) );

        if ( OperatingSystem.IsWindows() )
        {
            if ( !processRunner.CommandExists( WslCommand ) )
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic( $"'{WslCommand}' is not available on PATH." )
                );

            var distrosResult = await processRunner.RunAsync(
                new ProcessCommand(
                    WslCommand,
                    ["-l", "-q"],
                    StandardOutputEncoding: Encoding.Unicode,
                    StandardErrorEncoding: Encoding.Unicode
                ),
                cancellationToken
            );
            if ( distrosResult.ExitCode != 0 )
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic(
                        $"Could not list WSL distributions: {SelectProcessError( distrosResult )}"
                    )
                );

            if (
                !distrosResult
                    .StandardOutput.Split(
                        ['\r', '\n'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                    .Contains( Distribution, StringComparer.OrdinalIgnoreCase )
            )
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic( $"WSL distribution '{Distribution}' was not found." )
                );
        }
        else if ( !processRunner.CommandExists( AptGetCommand ) )
        {
            return ModuleInspectionResult.Failure(
                new ModuleDiagnostic( $"'{AptGetCommand}' is not available on PATH." )
            );
        }

        return ModuleInspectionResult.Success();
    }

    public override async ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameterError = ValidateParameters();
        if ( parameterError is not null )
            return ModuleResult.Failure( parameterError );

        DpkgPackageState installed;
        try
        {
            installed = await GetInstalledStateAsync(
                PackageName,
                Distribution,
                cancellationToken
            );
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

        var changed = shouldExist
            ? !installed.IsInstalled || needsVersionChange
            : installed.IsPresent;
        if ( Context.Mode == ExecutionMode.Check )
            return changed
                ? ModuleResult.ChangedResult( $"apt package '{PackageName}' would change." )
                : ModuleResult.NoChange(
                    $"apt package '{PackageName}' is already in the desired state."
                );

        if ( !changed )
            return ModuleResult.NoChange(
                $"apt package '{PackageName}' is already in the desired state."
            );

        var packageId = string.IsNullOrWhiteSpace( Version )
            ? PackageName
            : $"{PackageName}={Version}";
        var command = shouldExist
            ? BuildAptCommand( "install", packageId, Sudo )
            : BuildAptCommand( "remove", PackageName, Sudo );

        var result = await RunAptCommandAsync( command, Distribution, cancellationToken );
        if ( result.ExitCode != 0 )
            return ModuleResult.Failure( SelectProcessError( result ) );

        return ModuleResult.ChangedResult( $"apt package '{PackageName}' was updated." );
    }

    private async Task<DpkgPackageState> GetInstalledStateAsync(
        string packageName,
        string? distribution,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAptCommandAsync(
            ["dpkg-query", "-W", "-f=${db:Status-Status}\t${Version}\n", "--", packageName],
            distribution,
            cancellationToken
        );

        if ( result.ExitCode == 1 )
            return DpkgPackageState.Missing;

        if ( result.ExitCode != 0 )
            throw new ConfigurationException(
                $"Could not query apt package '{packageName}': {SelectProcessError( result )}"
            );

        return DpkgPackageState.Parse( result.StandardOutput );
    }

    private Task<ProcessResult> RunAptCommandAsync(
        IReadOnlyList<string> command,
        string? distribution,
        CancellationToken cancellationToken
    )
    {
        if ( OperatingSystem.IsWindows() )
        {
            var arguments = new List<string> { "-d", distribution!, "--exec" };
            arguments.AddRange( command );
            return processRunner.RunAsync(
                new ProcessCommand( WslCommand, arguments ),
                cancellationToken
            );
        }

        return processRunner.RunAsync(
            new ProcessCommand( command[0], command.Skip( 1 ).ToArray() ),
            cancellationToken
        );
    }

    private IReadOnlyList<string> BuildAptCommand( string action, string packageName, bool useSudo )
    {
        var arguments = new List<string>();
        if ( useSudo )
            arguments.Add( "sudo" );

        arguments.Add( AptGetCommand );
        arguments.Add( action );
        arguments.Add( "-y" );
        arguments.Add( "--" );
        arguments.Add( packageName );
        return arguments;
    }

    private string? ValidateParameters()
    {
        if ( State.Trim().ToLowerInvariant() is not ("present" or "absent") )
            return "Parameter 'state' must be 'present' or 'absent'.";

        if ( string.IsNullOrWhiteSpace( PackageName ) || !Regex.IsMatch(
            PackageName,
            @"\A[a-z0-9][a-z0-9+.-]*(?::[a-z0-9][a-z0-9-]*)?\z",
            RegexOptions.CultureInvariant
        ) )
            return "Parameter 'name' must be a Debian package name, optionally name:architecture.";

        if ( OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace( Distribution ) )
            return "apt on Windows requires a 'distribution' parameter.";

        return null;
    }

    private static string SelectProcessError( ProcessResult result ) =>
        !string.IsNullOrWhiteSpace( result.StandardError )
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace( result.StandardOutput )
                ? result.StandardOutput.Trim()
                : "apt failed.";
}
