using ConfigTool.Abstractions;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ConfigTool.Modules.Apt;

public sealed class AptModule : IConfigModule
{
    public ModuleDescriptor Descriptor { get; } =
        new("apt", "1.0.0", "Installs packages through apt locally or inside a WSL distribution.");

    public async ValueTask<ModuleInspectionResult> InspectAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CommandExists("wsl"))
            {
                return ModuleInspectionResult.Failure(new ModuleDiagnostic("wsl.exe is not available on PATH."));
            }

            var distribution = request.Parameters.GetOptionalString("distribution");
            if (string.IsNullOrWhiteSpace(distribution))
            {
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic("apt on Windows requires a 'distribution' parameter."));
            }

            var distrosResult = await RunAsync("wsl", ["-l", "-q"], Environment.CurrentDirectory, cancellationToken);
            if (distrosResult.ExitCode != 0 ||
                !distrosResult.StandardOutput
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(distribution, StringComparer.OrdinalIgnoreCase))
            {
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic($"WSL distribution '{distribution}' was not found."));
            }
        }
        else if (!CommandExists("apt-get"))
        {
            return ModuleInspectionResult.Failure(new ModuleDiagnostic("apt-get is not available on PATH."));
        }

        return ModuleInspectionResult.Success();
    }

    public async ValueTask<ModuleResult> ExecuteAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var packageName = request.Parameters.RequireString("name");
            var state = NormalizeState(request.Parameters.GetOptionalString("state"));
            var version = request.Parameters.GetOptionalString("version");
            var distribution = request.Parameters.GetOptionalString("distribution");
            var useSudo = request.Parameters.GetBoolean("sudo", true);

            var installed = await GetInstalledStateAsync(packageName, distribution, cancellationToken);
            var shouldExist = state == "present";
            var needsVersionChange = shouldExist &&
                !string.IsNullOrWhiteSpace(version) &&
                !string.Equals(installed.Version, version, StringComparison.OrdinalIgnoreCase);

            var changed = shouldExist != installed.IsInstalled || needsVersionChange;
            if (request.Mode == ExecutionMode.Check)
            {
                return changed
                    ? ModuleResult.ChangedResult($"apt package '{packageName}' would change.")
                    : ModuleResult.NoChange($"apt package '{packageName}' is already in the desired state.");
            }

            if (!changed)
            {
                return ModuleResult.NoChange($"apt package '{packageName}' is already in the desired state.");
            }

            var packageId = string.IsNullOrWhiteSpace(version)
                ? packageName
                : $"{packageName}={version}";
            var command = shouldExist
                ? BuildAptCommand("install", packageId, useSudo)
                : BuildAptCommand("remove", packageName, useSudo);

            var result = await RunAptCommandAsync(command, distribution, cancellationToken);
            if (result.ExitCode != 0)
            {
                return ModuleResult.Failure(SelectProcessError(result));
            }

            return ModuleResult.ChangedResult($"apt package '{packageName}' was updated.");
        }
        catch (ConfigurationException exception)
        {
            return ModuleResult.Failure(exception.Message);
        }
    }

    private async Task<(bool IsInstalled, string? Version)> GetInstalledStateAsync(
        string packageName,
        string? distribution,
        CancellationToken cancellationToken)
    {
        var result = await RunAptCommandAsync(
            ["dpkg-query", "-W", "-f=${Version}", packageName],
            distribution,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return (false, null);
        }

        var version = result.StandardOutput.Trim();
        return (!string.IsNullOrWhiteSpace(version), version);
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAptCommandAsync(
        IReadOnlyList<string> command,
        string? distribution,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var arguments = new List<string> { "-d", distribution!, "--" };
            arguments.AddRange(command);
            return await RunAsync("wsl", arguments, Environment.CurrentDirectory, cancellationToken);
        }

        return await RunAsync(command[0], command.Skip(1).ToArray(), Environment.CurrentDirectory, cancellationToken);
    }

    private static IReadOnlyList<string> BuildAptCommand(string action, string packageName, bool useSudo)
    {
        var arguments = new List<string>();
        if (useSudo)
        {
            arguments.Add("sudo");
        }

        arguments.Add("apt-get");
        arguments.Add(action);
        arguments.Add("-y");
        arguments.Add(packageName);
        return arguments;
    }

    private static string NormalizeState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "present" : state.Trim().ToLowerInvariant();

    private static string SelectProcessError((int ExitCode, string StandardOutput, string StandardError) result) =>
        !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardOutput.Trim()
                : "apt failed.";

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
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
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
