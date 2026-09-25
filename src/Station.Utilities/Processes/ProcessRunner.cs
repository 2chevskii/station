using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Station.Abstractions.FileSystem;

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
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory =
                command.WorkingDirectory ?? AbsolutePath.CurrentWorkingDirectory.Value,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
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

        await Task.WhenAll( stdoutTask, stderrTask, process.WaitForExitAsync( cancellationToken ) );

        return new ProcessResult( process.ExitCode, stdoutTask.Result, stderrTask.Result );
    }
}