using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Station.Abstractions.FileSystem;
using Station.Abstractions.Processes;

using TruePath;

namespace Station.Utilities.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public bool CommandExists( string commandName ) => CommandLocator.Exists( commandName );

    public async Task<ProcessResult> RunAsync(
        ProcessCommand command,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory =
                command.WorkingDirectory ?? AbsolutePath.CurrentWorkingDirectory.Value,
            StandardOutputEncoding = command.StandardOutputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding = command.StandardErrorEncoding ?? Encoding.UTF8,
        };

        foreach ( var argument in command.Arguments )
            startInfo.ArgumentList.Add( argument );

        if ( command.Environment is not null )
            foreach ( var (key, value) in command.Environment )
            {
                if ( value is null )
                {
                    startInfo.Environment.Remove( key );
                    continue;
                }

                startInfo.Environment[key] = value;
            }

        startInfo.Environment.TryGetValue( "PATH", out var searchPath );
        startInfo.Environment.TryGetValue( "PATHEXT", out var executableExtensions );
        var executable = CommandLocator.Find(
            command.FileName,
            startInfo.WorkingDirectory,
            searchPath ?? string.Empty,
            executableExtensions ?? ".COM;.EXE;.BAT;.CMD"
        );
        if ( executable is null )
            return new ProcessResult( -1, string.Empty, $"Command '{command.FileName}' was not found." );

        startInfo.FileName = executable;
        using var process = new Process();
        process.StartInfo = startInfo;

        try
        {
            process.Start();
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or Win32Exception )
        {
            return new ProcessResult( -1, string.Empty, exception.Message );
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync( cancellationToken );
        var stderrTask = process.StandardError.ReadToEndAsync( cancellationToken );

        try
        {
            await process.WaitForExitAsync( cancellationToken );
            await Task.WhenAll( stdoutTask, stderrTask );
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            await StopAsync( process );
            try
            {
                await Task.WhenAll( stdoutTask, stderrTask );
            }
            catch ( OperationCanceledException )
            {
                // Observe both cancelled reads before disposing the redirected streams.
            }

            throw;
        }

        return new ProcessResult( process.ExitCode, stdoutTask.Result, stderrTask.Result );
    }

    private static async Task StopAsync( Process process )
    {
        try
        {
            if ( !process.HasExited )
                process.Kill( entireProcessTree: true );
        }
        catch ( Exception exception ) when (
            exception is InvalidOperationException or Win32Exception && process.HasExited
        )
        {
            // The process may exit between the state check and the termination request.
        }

        await process.WaitForExitAsync( CancellationToken.None );
    }
}
