using System.Diagnostics;

using Station.Abstractions.FileSystem;
using Station.Abstractions.Processes;
using Station.Cli;

using TruePath;
using TruePath.SystemIo;

namespace Station.IntegrationTests.Cli;

internal static class CliRunner
{
    public static async Task<ProcessResult> RunAsync( params string[] arguments )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = FindRepositoryRoot(),
        };
        startInfo.ArgumentList.Add( typeof( StationConsole ).Assembly.Location );
        foreach ( var argument in arguments )
            startInfo.ArgumentList.Add( argument );

        using var process = Process.Start( startInfo )!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 30 ) );
            return new ProcessResult( process.ExitCode, await stdout, await stderr );
        }
        finally
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
                await process.WaitForExitAsync();
            }
        }
    }

    public static string FindRepositoryRoot()
    {
        AbsolutePath? directory = AppContext.BaseDirectory.AsAbsolutePath();
        while ( directory is { } current && !(current / "Station.slnx").ExistsFile() )
            directory = current.Parent;

        return directory?.Value
            ?? throw new InvalidOperationException( "Could not locate the repository root." );
    }
}
