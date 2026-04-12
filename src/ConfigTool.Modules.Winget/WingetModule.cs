using ConfigTool.Abstractions;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ConfigTool.Modules.Winget;

public sealed class WingetModule : IConfigModule
{
    public ModuleDescriptor Descriptor { get; } =
        new("winget", "1.0.0", "Installs packages through winget.");

    public ValueTask<ModuleInspectionResult> InspectAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!CommandExists("winget"))
        {
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure(new ModuleDiagnostic("winget is not available on PATH.")));
        }

        var scope = request.Parameters.GetOptionalString("scope");
        var privilege = string.Equals(scope, "machine", StringComparison.OrdinalIgnoreCase)
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
            var packageId = request.Parameters.RequireString("id");
            var state = NormalizeState(request.Parameters.GetOptionalString("state"));
            var version = request.Parameters.GetOptionalString("version");
            var scope = request.Parameters.GetOptionalString("scope");

            var installed = await GetInstalledStateAsync(packageId, version, cancellationToken);
            var shouldExist = state == "present";
            var needsVersionChange = shouldExist &&
                !string.IsNullOrWhiteSpace(version) &&
                !string.Equals(installed.Version, version, StringComparison.OrdinalIgnoreCase);

            var changed = shouldExist != installed.IsInstalled || needsVersionChange;
            if (request.Mode == ExecutionMode.Check)
            {
                return changed
                    ? ModuleResult.ChangedResult($"winget package '{packageId}' would change.")
                    : ModuleResult.NoChange($"winget package '{packageId}' is already in the desired state.");
            }

            if (!changed)
            {
                return ModuleResult.NoChange($"winget package '{packageId}' is already in the desired state.");
            }

            var arguments = state == "present"
                ? CreateInstallArguments(packageId, version, scope)
                : CreateUninstallArguments(packageId);
            var result = await RunAsync("winget", arguments, cancellationToken);
            if (result.ExitCode != 0)
            {
                return ModuleResult.Failure(SelectProcessError(result));
            }

            return ModuleResult.ChangedResult($"winget package '{packageId}' was updated.");
        }
        catch (ConfigurationException exception)
        {
            return ModuleResult.Failure(exception.Message);
        }
    }

    private async Task<(bool IsInstalled, string? Version)> GetInstalledStateAsync(
        string packageId,
        string? version,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            "winget",
            [
                "list",
                "--id",
                packageId,
                "--exact",
                "--disable-interactivity",
                "--accept-source-agreements",
            ],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return (false, null);
        }

        var installed = result.StandardOutput.Contains(packageId, StringComparison.OrdinalIgnoreCase);
        var installedVersion = string.IsNullOrWhiteSpace(version) ||
                               !result.StandardOutput.Contains(version, StringComparison.OrdinalIgnoreCase)
            ? null
            : version;
        return (installed, installedVersion);
    }

    private static string NormalizeState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "present" : state.Trim().ToLowerInvariant();

    private static IReadOnlyList<string> CreateInstallArguments(string id, string? version, string? scope)
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
    [
        "uninstall",
        "--id",
        id,
        "--exact",
        "--disable-interactivity",
    ];

    private static string SelectProcessError((int ExitCode, string StandardOutput, string StandardError) result) =>
        !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardOutput.Trim()
                : "winget failed.";

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
            var directPath = Path.Combine(directory, commandName);
            if (File.Exists(directPath))
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
