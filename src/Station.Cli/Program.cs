using Station.Abstractions;
using Station.Core;
using System.CommandLine;
using System.CommandLine.Parsing;

using Station.Abstractions.Enums;
using Station.Core.Execution;
using Station.Core.Plugins;

var configOption = new Option<DirectoryInfo?>("--config")
{
    Description = "Path to the config directory",
    DefaultValueFactory = _ => new DirectoryInfo(Environment.CurrentDirectory),
};

var setOption = new Option<string[]>("--set")
{
    Description = "Override a variable with key=value.",
    AllowMultipleArgumentsPerToken = true,
};

var rootCommand = new RootCommand("Manage local workstation configuration.");
rootCommand.Subcommands.Add(CreateRestoreCommand());
rootCommand.Subcommands.Add(CreateExplainCommand());
rootCommand.Subcommands.Add(CreateCommand("validate", "Validate config and plugins.", ExecutionMode.Validate));
rootCommand.Subcommands.Add(CreateCommand("check", "Dry-run the selected configuration.", ExecutionMode.Check));
rootCommand.Subcommands.Add(CreateCommand("apply", "Apply the selected configuration.", ExecutionMode.Apply));

return await rootCommand.Parse(args).InvokeAsync();

Command CreateRestoreCommand()
{
    var command = new Command("restore", "Resolve and cache declared plugins.");
    command.Options.Add(configOption);
    command.Options.Add(setOption);
    command.SetAction(async (parseResult, cancellationToken) =>
    {
        var configRoot = parseResult.GetValue(configOption)?.FullName ?? Environment.CurrentDirectory;
        var cliVariables = ParseCliVariables(parseResult.GetValue(setOption));
        var engine = new ConfigEngine();
        var result = await engine.RestoreAsync(
            configRoot,
            cliVariables,
            static message => Console.WriteLine($"[station] {message}"),
            cancellationToken);

        WriteRestoreResult(result);
        return result.Succeeded ? 0 : 1;
    });
    return command;
}

Command CreateExplainCommand()
{
    var command = new Command("explain", "Show the static task plan, bound parameters, and visible variables.");
    command.Options.Add(configOption);
    command.Options.Add(setOption);
    command.SetAction(async (parseResult, cancellationToken) =>
    {
        var configRoot = parseResult.GetValue(configOption)?.FullName ?? Environment.CurrentDirectory;
        var cliVariables = ParseCliVariables(parseResult.GetValue(setOption));
        var engine = new ConfigEngine();
        var result = await engine.ExplainAsync(
            configRoot,
            cliVariables,
            static message => Console.WriteLine($"[station] {message}"),
            cancellationToken);

        WriteExplainResult(result);
        return result.Succeeded ? 0 : 1;
    });
    return command;
}

Command CreateCommand(string name, string description, ExecutionMode mode)
{
    var command = new Command(name, description);
    command.Options.Add(configOption);
    command.Options.Add(setOption);
    command.SetAction((parseResult, cancellationToken) =>
        ExecuteCommandAsync(parseResult, mode, cancellationToken));
    return command;
}

async Task<int> ExecuteCommandAsync(ParseResult parseResult, ExecutionMode mode, CancellationToken cancellationToken)
{
    var configRoot = parseResult.GetValue(configOption)?.FullName ?? Environment.CurrentDirectory;
    var cliVariables = ParseCliVariables(parseResult.GetValue(setOption));

    var engine = new ConfigEngine();
    var result = await engine.RunAsync(
        configRoot,
        mode,
        cliVariables,
        static message => Console.WriteLine($"[station] {message}"),
        cancellationToken);

    WriteResult(result);
    return result.Succeeded ? 0 : 1;
}

static IReadOnlyDictionary<string, string> ParseCliVariables(string[]? values)
{
    var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var value in values ?? [])
    {
        var separatorIndex = value.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            throw new InvalidOperationException(
                $"Invalid '--set' value '{value}'. Expected the form key=value.");
        }

        var key = value[..separatorIndex].Trim();
        var entryValue = value[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"Invalid '--set' value '{value}'. Variable name must not be empty.");
        }

        variables[key] = entryValue;
    }

    return variables;
}

static void WriteResult(EngineRunResult result)
{
    var output = result.Succeeded ? Console.Out : Console.Error;
    output.WriteLine(result.Message);

    foreach (var diagnostic in result.Diagnostics)
    {
        output.WriteLine($"  - {diagnostic}");
    }

    foreach (var task in result.Tasks)
    {
        var status = task.Result.Failed
            ? "failed"
            : task.Result.Changed
                ? "changed"
                : task.Result.Skipped
                    ? "skipped"
                    : "ok";
        var roleLabel = string.IsNullOrWhiteSpace(task.RoleName) ? "root" : task.RoleName;
        output.WriteLine($"  * {task.TaskName} [{task.ModuleName}] ({roleLabel}): {status}");
        if (!string.IsNullOrWhiteSpace(task.Result.Message))
        {
            output.WriteLine($"    {task.Result.Message}");
        }
    }
}

static void WriteRestoreResult(PluginRestoreResult result)
{
    var output = result.Succeeded ? Console.Out : Console.Error;
    output.WriteLine(result.Message);

    foreach (var diagnostic in result.Diagnostics)
    {
        output.WriteLine($"  - {diagnostic}");
    }

    foreach (var plugin in result.Plugins)
    {
        var sourceKind = plugin.IsLocal ? "local" : plugin.CacheHit ? "cache" : "downloaded";
        output.WriteLine(
            $"  * {plugin.PluginName ?? plugin.Declaration} {plugin.Version ?? "unknown"} [{sourceKind}] => {plugin.PluginRoot}");
    }
}

static void WriteExplainResult(ExplainResult result)
{
    var output = result.Succeeded ? Console.Out : Console.Error;
    output.WriteLine(result.Message);

    foreach (var diagnostic in result.Diagnostics)
    {
        output.WriteLine($"  - {diagnostic}");
    }

    if (!string.IsNullOrWhiteSpace(result.Diagram))
    {
        output.WriteLine(result.Diagram);
    }
}
