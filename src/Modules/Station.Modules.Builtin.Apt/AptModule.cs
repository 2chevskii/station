using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Station.Abstractions.Attributes;
using Station.Abstractions.Enums;
using Station.Abstractions.Models;
using TruePath;
using TruePath.SystemIo;

namespace Station.Modules.Builtin.Apt;

[Module("apt", "Installs packages through apt locally or inside a WSL distribution.")]
public sealed class AptModule : ConfigModule
{
    public AptModule(ModuleContext context)
        : base(context) { }

    [Parameter("name", Required = true)]
    public string PackageName { get; set; } = string.Empty;

    [Parameter]
    public string State { get; set; } = "present";

    [Parameter]
    public string? Version { get; set; }

    [Parameter]
    public string? Distribution { get; set; }

    [Parameter]
    public bool Sudo { get; set; } = true;

    [Parameter]
    public string WslCommand { get; set; } = "wsl";

    [Parameter]
    public string AptGetCommand { get; set; } = "apt-get";

    public override async ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CommandExists(WslCommand))
            {
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic($"'{WslCommand}' is not available on PATH.")
                );
            }

            if (string.IsNullOrWhiteSpace(Distribution))
            {
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic("apt on Windows requires a 'distribution' parameter.")
                );
            }

            var distrosResult = await RunAsync(
                WslCommand,
                ["-l", "-q"],
                AbsolutePath.CurrentWorkingDirectory.Value,
                cancellationToken
            );
            if (
                distrosResult.ExitCode != 0
                || !distrosResult
                    .StandardOutput.Split(
                        Environment.NewLine,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                    .Contains(Distribution, StringComparer.OrdinalIgnoreCase)
            )
            {
                return ModuleInspectionResult.Failure(
                    new ModuleDiagnostic($"WSL distribution '{Distribution}' was not found.")
                );
            }
        }
        else if (!CommandExists(AptGetCommand))
        {
            return ModuleInspectionResult.Failure(
                new ModuleDiagnostic($"'{AptGetCommand}' is not available on PATH.")
            );
        }

        return ModuleInspectionResult.Success();
    }

    public override async ValueTask<ModuleResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var state = NormalizeState(State);
        var installed = await GetInstalledStateAsync(PackageName, Distribution, cancellationToken);
        var shouldExist = state == "present";
        var needsVersionChange =
            shouldExist
            && !string.IsNullOrWhiteSpace(Version)
            && !string.Equals(installed.Version, Version, StringComparison.OrdinalIgnoreCase);

        var changed = shouldExist != installed.IsInstalled || needsVersionChange;
        if (Context.Mode == ExecutionMode.Check)
        {
            return changed
                ? ModuleResult.ChangedResult($"apt package '{PackageName}' would change.")
                : ModuleResult.NoChange(
                    $"apt package '{PackageName}' is already in the desired state."
                );
        }

        if (!changed)
        {
            return ModuleResult.NoChange(
                $"apt package '{PackageName}' is already in the desired state."
            );
        }

        var packageId = string.IsNullOrWhiteSpace(Version)
            ? PackageName
            : $"{PackageName}={Version}";
        var command = shouldExist
            ? BuildAptCommand("install", packageId, Sudo)
            : BuildAptCommand("remove", PackageName, Sudo);

        var result = await RunAptCommandAsync(command, Distribution, cancellationToken);
        if (result.ExitCode != 0)
        {
            return ModuleResult.Failure(SelectProcessError(result));
        }

        return ModuleResult.ChangedResult($"apt package '{PackageName}' was updated.");
    }

    private async Task<(bool IsInstalled, string? Version)> GetInstalledStateAsync(
        string packageName,
        string? distribution,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAptCommandAsync(
            ["dpkg-query", "-W", "-f=${Version}", packageName],
            distribution,
            cancellationToken
        );

        if (result.ExitCode != 0)
        {
            return (false, null);
        }

        var version = result.StandardOutput.Trim();
        return (!string.IsNullOrWhiteSpace(version), version);
    }

    private async Task<(
        int ExitCode,
        string StandardOutput,
        string StandardError
    )> RunAptCommandAsync(
        IReadOnlyList<string> command,
        string? distribution,
        CancellationToken cancellationToken
    )
    {
        if (OperatingSystem.IsWindows())
        {
            var arguments = new List<string> { "-d", distribution!, "--" };
            arguments.AddRange(command);
            return await RunAsync(
                WslCommand,
                arguments,
                AbsolutePath.CurrentWorkingDirectory.Value,
                cancellationToken
            );
        }

        return await RunAsync(
            command[0],
            command.Skip(1).ToArray(),
            AbsolutePath.CurrentWorkingDirectory.Value,
            cancellationToken
        );
    }

    private IReadOnlyList<string> BuildAptCommand(string action, string packageName, bool useSudo)
    {
        var arguments = new List<string>();
        if (useSudo)
        {
            arguments.Add("sudo");
        }

        arguments.Add(AptGetCommand);
        arguments.Add(action);
        arguments.Add("-y");
        arguments.Add(packageName);
        return arguments;
    }

    private static string NormalizeState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "present" : state.Trim().ToLowerInvariant();

    private static string SelectProcessError(
        (int ExitCode, string StandardOutput, string StandardError) result
    ) =>
        !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError.Trim()
        : !string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardOutput.Trim()
        : "apt failed.";

    private static bool CommandExists(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        if (new LocalPath(commandName).IsAbsolute)
        {
            return new LocalPath(commandName).ResolveToCurrentDirectory().ExistsFile();
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM;.PS1").Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            : [string.Empty];

        foreach (var directory in pathEntries)
        {
            if ((new LocalPath(directory) / commandName).ResolveToCurrentDirectory().ExistsFile())
            {
                return true;
            }

            foreach (var extension in extensions)
            {
                if ((new LocalPath(directory) / (commandName + extension)).ResolveToCurrentDirectory().ExistsFile())
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
        CancellationToken cancellationToken
    )
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
