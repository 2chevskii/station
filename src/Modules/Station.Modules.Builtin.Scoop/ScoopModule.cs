using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Station.Abstractions.Enums;
using Station.Abstractions.Interfaces;
using Station.Abstractions.Models;

namespace Station.Modules.Builtin.Scoop;

public sealed class ScoopModule : IConfigModule
{
    public ModuleDescriptor Descriptor { get; } =
        new("scoop", "1.0.0", "Installs packages through Scoop.");

    public ValueTask<ModuleInspectionResult> InspectAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!CommandExists("scoop"))
        {
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure(new ModuleDiagnostic("scoop is not available on PATH.")));
        }

        var privilege = request.Parameters.GetBoolean("global")
            ? PrivilegeRequirement.Administrator
            : PrivilegeRequirement.User;

        return ValueTask.FromResult(ModuleInspectionResult.Success(privilege));
    }

    public async ValueTask<ModuleResult> ExecuteAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var packageName = request.Parameters.RequireString("name");
            var state = NormalizeState(request.Parameters.GetOptionalString("state"));
            var isGlobal = request.Parameters.GetBoolean("global");

            var listResult = await RunAsync("scoop", ["list"], cancellationToken);
            var installed = listResult.ExitCode == 0 &&
                            listResult.StandardOutput.Contains(packageName, StringComparison.OrdinalIgnoreCase);
            var shouldExist = state == "present";
            var changed = shouldExist != installed;

            if (request.Mode == ExecutionMode.Check)
            {
                return changed
                    ? ModuleResult.ChangedResult($"scoop package '{packageName}' would change.")
                    : ModuleResult.NoChange($"scoop package '{packageName}' is already in the desired state.");
            }

            if (!changed)
            {
                return ModuleResult.NoChange($"scoop package '{packageName}' is already in the desired state.");
            }

            var arguments = new List<string> { shouldExist ? "install" : "uninstall" };
            if (isGlobal)
            {
                arguments.Add("--global");
            }

            arguments.Add(packageName);

            var result = await RunAsync("scoop", arguments, cancellationToken);
            if (result.ExitCode != 0)
            {
                return ModuleResult.Failure(SelectProcessError(result));
            }

            return ModuleResult.ChangedResult($"scoop package '{packageName}' was updated.");
        }
        catch (ConfigurationException exception)
        {
            return ModuleResult.Failure(exception.Message);
        }
    }

    private static string NormalizeState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "present" : state.Trim().ToLowerInvariant();

    private static string SelectProcessError((int ExitCode, string StandardOutput, string StandardError) result) =>
        !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardOutput.Trim()
                : "scoop failed.";

    private static bool CommandExists(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM;.PS1")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in pathEntries)
        {
            if (File.Exists(Path.Combine(directory, commandName)))
            {
                return true;
            }

            foreach (var extension in extensions)
            {
                if (File.Exists(Path.Combine(directory, commandName + extension)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return (-1, string.Empty, exception.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellationToken));
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
