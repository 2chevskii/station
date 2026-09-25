using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Station.Abstractions.Attributes;
using Station.Abstractions.Enums;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Models;
using TruePath;
using TruePath.SystemIo;

namespace Station.Modules.Builtin.Script;

[Module("script", "Runs local scripts with a JSON request contract.")]
public sealed class ScriptModule : ConfigModule
{
    public ScriptModule(ModuleContext context)
        : base(context) { }

    [Parameter("path", Required = true)]
    public string ScriptPath { get; set; } = string.Empty;

    [Parameter]
    public string Runner { get; set; } = "pwsh";

    [Parameter]
    public bool RequiresAdmin { get; set; }

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            var scriptPath = ResolveScriptPath();
            if (!scriptPath.ExistsFile())
            {
                return ValueTask.FromResult(
                    ModuleInspectionResult.Failure(
                        new ModuleDiagnostic($"Script '{scriptPath}' does not exist.")
                    )
                );
            }

            var runner = Runner.ToLowerInvariant();
            if (!CommandExists(runner))
            {
                return ValueTask.FromResult(
                    ModuleInspectionResult.Failure(
                        new ModuleDiagnostic($"Runner '{runner}' is not available on PATH.")
                    )
                );
            }

            var privilege = RequiresAdmin
                ? PrivilegeRequirement.Administrator
                : PrivilegeRequirement.User;

            return ValueTask.FromResult(ModuleInspectionResult.Success(privilege));
        }
        catch (ConfigurationException exception)
        {
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure(new ModuleDiagnostic(exception.Message))
            );
        }
    }

    public override async ValueTask<ModuleResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var scriptPath = ResolveScriptPath();
            var requestPayload = CreatePayload();
            var requestFilePath = Temporary.CreateTempFile();
            await requestFilePath.WriteAllTextAsync(requestPayload.ToJsonString(), cancellationToken);

            try
            {
                var runner = Runner.ToLowerInvariant();
                var result = await RunAsync(
                    runner,
                    ["-NoLogo", "-NoProfile", "-File", scriptPath.Value, requestFilePath.Value],
                    Context.WorkingDirectory,
                    new Dictionary<string, string?>
                    {
                        ["CONFIG_TOOL_REQUEST_PATH"] = requestFilePath.Value,
                    },
                    cancellationToken
                );
                if (result.ExitCode != 0)
                {
                    return ModuleResult.Failure(SelectProcessError(result));
                }

                return ParseResult(result.StandardOutput);
            }
            finally
            {
                requestFilePath.Delete();
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
            payload =
                JsonNode.Parse(stdout)?.AsObject()
                ?? throw new JsonException("Output was not a JSON object.");
        }
        catch (JsonException exception)
        {
            return ModuleResult.Failure(
                $"Script module returned invalid JSON: {exception.Message}"
            );
        }

        var changed = payload["changed"]?.GetValue<bool>() ?? false;
        var failed = payload["failed"]?.GetValue<bool>() ?? false;
        var skipped = payload["skipped"]?.GetValue<bool>() ?? false;
        var message = payload["message"]?.GetValue<string>();
        var outputs = payload["outputs"] as JsonObject;

        return new ModuleResult(changed, failed, skipped, message, outputs);
    }

    private JsonObject CreatePayload() =>
        new()
        {
            ["mode"] = Context.Mode.ToString().ToLowerInvariant(),
            ["taskName"] = Context.TaskName,
            ["roleName"] = Context.RoleName,
            ["variables"] =
                ConfigValueUtilities.ToJsonNode(
                    Context.Variables.Root,
                    Context.Variables,
                    Context.TemplateEngine
                ) ?? new JsonObject(),
        };

    private AbsolutePath ResolveScriptPath()
    {
        var taskRoot = Context.WorkingDirectory.AsLocalPath().ResolveToCurrentDirectory();
        var fullPath = taskRoot / ScriptPath;
        if (!IsWithinRoot(fullPath, taskRoot))
        {
            throw new ConfigurationException("Script path must stay within the task root.");
        }

        return fullPath;
    }

    private static bool IsWithinRoot(AbsolutePath path, AbsolutePath root)
    {
        for (AbsolutePath? current = path; current is { } candidate; current = candidate.Parent)
        {
            if (candidate.Equals(root, AbsolutePath.PlatformDefaultComparer))
            {
                return true;
            }
        }

        return false;
    }

    private static string SelectProcessError(
        (int ExitCode, string StandardOutput, string StandardError) result
    ) =>
        !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError.Trim()
        : !string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardOutput.Trim()
        : "Script execution failed.";

    private static bool CommandExists(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
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
            if ((directory.AsLocalPath() / commandName).ResolveToCurrentDirectory().ExistsFile())
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
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
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
