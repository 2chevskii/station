using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Station.Core.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public bool CommandExists(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        if (Path.IsPathRooted(commandName))
        {
            return File.Exists(commandName);
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM;.PS1")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in pathEntries)
        {
            var directPath = Path.Combine(directory, commandName);
            if (File.Exists(directPath))
            {
                return true;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, commandName + extension);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public async Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = command.WorkingDirectory ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (command.Environment is not null)
        {
            foreach (var (key, value) in command.Environment)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(key);
                    continue;
                }

                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return new ProcessResult(-1, string.Empty, exception.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellationToken));

        return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
