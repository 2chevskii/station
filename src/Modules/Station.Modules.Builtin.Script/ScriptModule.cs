using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Station.Abstractions.Enums;
using Station.Abstractions.Interfaces;
using Station.Abstractions.Models;

namespace Station.Modules.Builtin.Script;

public sealed class ScriptModule : IConfigModule
{
    public ModuleDescriptor Descriptor { get; } =
        new("script", "1.0.0", "Runs local scripts with a JSON request contract.");

    public ValueTask<ModuleInspectionResult> InspectAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var scriptPath = ResolveScriptPath(request);
            if (!File.Exists(scriptPath))
            {
                return ValueTask.FromResult(
                    ModuleInspectionResult.Failure(new ModuleDiagnostic($"Script '{scriptPath}' does not exist.")));
            }

            var runner = (request.Parameters.GetOptionalString("runner") ?? "pwsh").ToLowerInvariant();
            if (!CommandExists(runner))
            {
                return ValueTask.FromResult(
                    ModuleInspectionResult.Failure(new ModuleDiagnostic($"Runner '{runner}' is not available on PATH.")));
            }

            var privilege = request.Parameters.GetBoolean("requiresAdmin")
                ? PrivilegeRequirement.Administrator
                : PrivilegeRequirement.User;

            return ValueTask.FromResult(ModuleInspectionResult.Success(privilege));
        }
        catch (ConfigurationException exception)
        {
            return ValueTask.FromResult(ModuleInspectionResult.Failure(new ModuleDiagnostic(exception.Message)));
        }
    }

    public async ValueTask<ModuleResult> ExecuteAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var scriptPath = ResolveScriptPath(request);
            var requestPayload = CreatePayload(request);
            var requestFilePath = Path.GetTempFileName();
            await File.WriteAllTextAsync(requestFilePath, requestPayload.ToJsonString(), cancellationToken);

            try
            {
                var runner = (request.Parameters.GetOptionalString("runner") ?? "pwsh").ToLowerInvariant();
                var result = await RunAsync(
                    runner,
                    ["-NoLogo", "-NoProfile", "-File", scriptPath, requestFilePath],
                    request.TaskRoot,
                    new Dictionary<string, string?>
                    {
                        ["CONFIG_TOOL_REQUEST_PATH"] = requestFilePath,
                    },
                    cancellationToken);
                if (result.ExitCode != 0)
                {
                    return ModuleResult.Failure(SelectProcessError(result));
                }

                return ParseResult(result.StandardOutput);
            }
            finally
            {
                File.Delete(requestFilePath);
            }
        }
        catch (ConfigurationException exception)
        {
            return ModuleResult.Failure(exception.Message);
        }
    }

    private static ModuleResult ParseResult(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return ModuleResult.Failure("Script module produced no JSON output.");
        }

        JsonObject payload;
        try
        {
            payload = JsonNode.Parse(stdout)?.AsObject()
                ?? throw new JsonException("Output was not a JSON object.");
        }
        catch (JsonException exception)
        {
            return ModuleResult.Failure($"Script module returned invalid JSON: {exception.Message}");
        }

        var changed = payload["changed"]?.GetValue<bool>() ?? false;
        var failed = payload["failed"]?.GetValue<bool>() ?? false;
        var skipped = payload["skipped"]?.GetValue<bool>() ?? false;
        var message = payload["message"]?.GetValue<string>();
        var outputs = payload["outputs"] as JsonObject;

        return new ModuleResult(changed, failed, skipped, message, outputs);
    }

    private static JsonObject CreatePayload(ModuleExecutionRequest request) =>
        new()
        {
            ["mode"] = request.Mode.ToString().ToLowerInvariant(),
            ["taskName"] = request.TaskName,
            ["roleName"] = request.RoleName,
            ["parameters"] = request.Parameters.DeepClone(),
            ["variables"] = JsonSerializer.SerializeToNode(request.Variables),
        };

    private static string ResolveScriptPath(ModuleExecutionRequest request)
    {
        var relativePath = request.Parameters.RequireString("path");
        var fullPath = Path.GetFullPath(relativePath, request.TaskRoot);
        var taskRoot = Path.GetFullPath(request.TaskRoot);
        if (!fullPath.StartsWith(taskRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException("Script path must stay within the task root.");
        }

        return fullPath;
    }

    private static string SelectProcessError((int ExitCode, string StandardOutput, string StandardError) result) =>
        !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError.Trim()
            : !string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardOutput.Trim()
                : "Script execution failed.";

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
        IReadOnlyDictionary<string, string?> environment,
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

        foreach (var (key, value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
                continue;
            }

            startInfo.Environment[key] = value;
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
