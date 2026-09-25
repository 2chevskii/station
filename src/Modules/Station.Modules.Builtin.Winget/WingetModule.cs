using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Station.Abstractions.Attributes;
using Station.Abstractions.Enums;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Models;
using TruePath;
using TruePath.SystemIo;

namespace Station.Modules.Builtin.Winget;

[Module("winget", "Installs packages through winget.")]
public sealed class WingetModule : ConfigModule
{
    public WingetModule(ModuleContext context)
        : base(context) { }

    [Parameter("id", Required = true)]
    public string PackageId { get; set; } = string.Empty;

    [Parameter]
    public string State { get; set; } = "present";

    [Parameter]
    public string? Version { get; set; }

    [Parameter]
    public string? Scope { get; set; }

    [Parameter]
    public string Command { get; set; } = "winget";

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        if (!CommandExists(Command))
        {
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure(
                    new ModuleDiagnostic($"'{Command}' is not available on PATH.")
                )
            );
        }

        var privilege = string.Equals(Scope, "machine", StringComparison.OrdinalIgnoreCase)
            ? PrivilegeRequirement.Administrator
            : PrivilegeRequirement.User;

        return ValueTask.FromResult(ModuleInspectionResult.Success(privilege));
    }

    public override async ValueTask<ModuleResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var state = NormalizeState(State);
        var installed = await GetInstalledStateAsync(PackageId, Version, cancellationToken);
        var shouldExist = state == "present";
        var needsVersionChange =
            shouldExist
            && !string.IsNullOrWhiteSpace(Version)
            && !string.Equals(installed.Version, Version, StringComparison.OrdinalIgnoreCase);

        var changed = shouldExist != installed.IsInstalled || needsVersionChange;
        if (Context.Mode == ExecutionMode.Check)
        {
            return changed
                ? ModuleResult.ChangedResult($"winget package '{PackageId}' would change.")
                : ModuleResult.NoChange(
                    $"winget package '{PackageId}' is already in the desired state."
                );
        }

        if (!changed)
        {
            return ModuleResult.NoChange(
                $"winget package '{PackageId}' is already in the desired state."
            );
        }

        var arguments =
            state == "present"
                ? CreateInstallArguments(PackageId, Version, Scope)
                : CreateUninstallArguments(PackageId);
        var result = await RunAsync(Command, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            return ModuleResult.Failure(SelectProcessError(result));
        }

        return ModuleResult.ChangedResult($"winget package '{PackageId}' was updated.");
    }

    private async Task<(bool IsInstalled, string? Version)> GetInstalledStateAsync(
        string packageId,
        string? version,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(
            Command,
            [
                "list",
                "--id",
                packageId,
                "--exact",
                "--disable-interactivity",
                "--accept-source-agreements",
            ],
            cancellationToken
        );

        if (result.ExitCode != 0)
        {
            return (false, null);
        }

        var installed = result.StandardOutput.Contains(
            packageId,
            StringComparison.OrdinalIgnoreCase
        );
        var installedVersion =
            string.IsNullOrWhiteSpace(version)
            || !result.StandardOutput.Contains(version, StringComparison.OrdinalIgnoreCase)
                ? null
                : version;
        return (installed, installedVersion);
    }

    private static string NormalizeState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "present" : state.Trim().ToLowerInvariant();

    private static IReadOnlyList<string> CreateInstallArguments(
        string id,
        string? version,
        string? scope
    )
    {
        var arguments = new List<string>
        {
            "install",
            "--id",
            id,
            "--exact",
            "--disable-interactivity",
            "--accept-source-agreements",
            "--accept-package-agreements",
        };

        if (!string.IsNullOrWhiteSpace(version))
        {
            arguments.Add("--version");
            arguments.Add(version);
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            arguments.Add("--scope");
            arguments.Add(scope);
        }

        return arguments;
    }

    private static IReadOnlyList<string> CreateUninstallArguments(string id) =>
        ["uninstall", "--id", id, "--exact", "--disable-interactivity"];

    private static string SelectProcessError(
        (int ExitCode, string StandardOutput, string StandardError) result
    ) =>
        !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError.Trim()
        : !string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardOutput.Trim()
        : "winget failed.";

    private static bool CommandExists(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        if (commandName.AsLocalPath().IsAbsolute)
        {
            return commandName.AsLocalPath().ResolveToCurrentDirectory().ExistsFile();
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
            var directPath = (directory.AsLocalPath() / commandName).ResolveToCurrentDirectory();
            if (directPath.ExistsFile())
            {
                return true;
            }

            foreach (var extension in extensions)
            {
                if ((directory.AsLocalPath() / (commandName + extension)).ResolveToCurrentDirectory().ExistsFile())
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
