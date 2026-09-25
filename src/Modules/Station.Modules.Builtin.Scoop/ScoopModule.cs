using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Station.Abstractions.Execution;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Modules;

using TruePath;

namespace Station.Modules.Builtin.Scoop;

[Module( "scoop", "Installs packages through Scoop." )]
public sealed class ScoopModule : ConfigModule
{
    public ScoopModule( ModuleContext context )
        : base( context )
    {
    }

    [Parameter( "name", Required = true )] public string PackageName { get; set; } = string.Empty;

    [Parameter] public string State { get; set; } = "present";

    [Parameter( "global" )] public bool IsGlobal { get; set; }

    [Parameter] public string Command { get; set; } = "scoop";

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        if ( !CommandLocator.Exists( Command ) )
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
        var state = NormalizeState( State );
        var listResult = await RunAsync( Command, ["list"], cancellationToken );
        var installed =
            listResult.ExitCode == 0
            && listResult.StandardOutput.Contains( PackageName, StringComparison.OrdinalIgnoreCase );
        var shouldExist = state == "present";
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

        arguments.Add( PackageName );

        var result = await RunAsync( Command, arguments, cancellationToken );
        if ( result.ExitCode != 0 )
            return ModuleResult.Failure( SelectProcessError( result ) );

        return ModuleResult.ChangedResult( $"scoop package '{PackageName}' was updated." );
    }

    private static string NormalizeState( string? state ) =>
        string.IsNullOrWhiteSpace( state ) ? "present" : state.Trim().ToLowerInvariant();

    private static string SelectProcessError(
        (int ExitCode, string StandardOutput, string StandardError) result
    ) =>
        !string.IsNullOrWhiteSpace( result.StandardError )
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace( result.StandardOutput )
                ? result.StandardOutput.Trim()
                : "scoop failed.";

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = AbsolutePath.CurrentWorkingDirectory.Value,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach ( var argument in arguments )
            startInfo.ArgumentList.Add( argument );

        using var process = new Process();
        process.StartInfo = startInfo;
        try
        {
            process.Start();
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or Win32Exception )
        {
            return (-1, string.Empty, exception.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync( cancellationToken );
        var stderrTask = process.StandardError.ReadToEndAsync( cancellationToken );
        await Task.WhenAll( stdoutTask, stderrTask, process.WaitForExitAsync( cancellationToken ) );
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}