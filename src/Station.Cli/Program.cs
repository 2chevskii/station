using System.CommandLine;
using System.CommandLine.Parsing;
using Spectre.Console;
using Station.Abstractions;
using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Abstractions.FileSystem;
using Station.Core;
using Station.Core.Execution;
using Station.Core.Execution.Modules;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Core.Execution.Roles;
using Station.Core.Plugins;
using Station.Core.Plugins.Restoration;
using TruePath;

var stationConsole = StationConsole.CreateStandard();

var configOption = new Option<string>("--config")
{
    Description = "Path to the config directory",
    DefaultValueFactory = _ => AbsolutePath.CurrentWorkingDirectory.Value,
};

var setOption = new Option<string[]>("--set")
{
    Description = "Override a variable with key=value.",
    AllowMultipleArgumentsPerToken = true,
};

var rootCommand = new RootCommand("Manage local workstation configuration.");
rootCommand.Subcommands.Add(CreateRestoreCommand());
rootCommand.Subcommands.Add(CreateExplainCommand());
rootCommand.Subcommands.Add(
    CreateCommand("validate", "Validate config and plugins.", ExecutionMode.Validate)
);
rootCommand.Subcommands.Add(
    CreateCommand("check", "Dry-run the selected configuration.", ExecutionMode.Check)
);
rootCommand.Subcommands.Add(
    CreateCommand("apply", "Apply the selected configuration.", ExecutionMode.Apply)
);

return await rootCommand.Parse(args).InvokeAsync();

Command CreateRestoreCommand()
{
    var command = new Command("restore", "Resolve and cache declared plugins.");
    command.Options.Add(configOption);
    command.Options.Add(setOption);
    command.SetAction(
        async (parseResult, cancellationToken) =>
        {
            var configRoot = ResolveConfigRoot(parseResult);
            var cliVariables = ParseCliVariables(parseResult.GetValue(setOption));
            var engine = new ConfigEngine();
            var result = await engine.RestoreAsync(
                configRoot,
                cliVariables,
                stationConsole.WriteLogMessage,
                cancellationToken
            );

            stationConsole.WriteRestoreResult(result);
            return result.Succeeded ? 0 : 1;
        }
    );
    return command;
}

Command CreateExplainCommand()
{
    var command = new Command(
        "explain",
        "Show the static task plan, bound parameters, and visible variables."
    );
    command.Options.Add(configOption);
    command.Options.Add(setOption);
    command.SetAction(
        async (parseResult, cancellationToken) =>
        {
            var configRoot = ResolveConfigRoot(parseResult);
            var cliVariables = ParseCliVariables(parseResult.GetValue(setOption));
            var engine = new ConfigEngine();
            var result = await engine.ExplainAsync(
                configRoot,
                cliVariables,
                stationConsole.WriteLogMessage,
                cancellationToken
            );

            stationConsole.WriteExplainResult(result);
            return result.Succeeded ? 0 : 1;
        }
    );
    return command;
}

Command CreateCommand(string name, string description, ExecutionMode mode)
{
    var command = new Command(name, description);
    command.Options.Add(configOption);
    command.Options.Add(setOption);
    command.SetAction(
        (parseResult, cancellationToken) =>
            ExecuteCommandAsync(parseResult, mode, cancellationToken)
    );
    return command;
}

async Task<int> ExecuteCommandAsync(
    ParseResult parseResult,
    ExecutionMode mode,
    CancellationToken cancellationToken
)
{
    var configRoot = ResolveConfigRoot(parseResult);
    var cliVariables = ParseCliVariables(parseResult.GetValue(setOption));

    var engine = new ConfigEngine();
    var result = await engine.RunAsync(
        configRoot,
        mode,
        cliVariables,
        stationConsole.WriteLogMessage,
        cancellationToken
    );

    stationConsole.WriteResult(result);
    return result.Succeeded ? 0 : 1;
}

string ResolveConfigRoot(ParseResult parseResult) =>
    parseResult.GetValue(configOption)!.AsLocalPath().ResolveToCurrentDirectory().Value;

static IReadOnlyDictionary<string, string> ParseCliVariables(string[]? values)
{
    var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var value in values ?? [])
    {
        var separatorIndex = value.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            throw new InvalidOperationException(
                $"Invalid '--set' value '{value}'. Expected the form key=value."
            );
        }

        var key = value[..separatorIndex].Trim();
        var entryValue = value[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"Invalid '--set' value '{value}'. Variable name must not be empty."
            );
        }

        variables[key] = entryValue;
    }

    return variables;
}
