using System.CommandLine;

using Station.Abstractions.Execution;
using Station.Abstractions.FileSystem;
using Station.Cli;
using Station.Core.Execution.Inventory;
using Station.Core.Execution.Running;

using TruePath;

var stationConsole = StationConsole.CreateStandard();

var configOption = new Option<string>( "--config" )
{
    Description = "Path to the config directory",
    DefaultValueFactory = _ => AbsolutePath.CurrentWorkingDirectory.Value,
};

var setOption = new Option<string[]>( "--set" )
{
    Description = "Override a variable with key=value.",
    AllowMultipleArgumentsPerToken = true,
};

var pluginCacheDirectoryOption = new Option<string?>( "--plugin-cache-dir" )
{
    Description = "Override the plugin cache directory.",
};

var rootCommand = new RootCommand( "Manage local workstation configuration." );
rootCommand.Subcommands.Add( CreateRestoreCommand() );
rootCommand.Subcommands.Add( CreateExplainCommand() );
rootCommand.Subcommands.Add( CreateListCommand() );
rootCommand.Subcommands.Add(
    CreateCommand( "validate", "Validate config and plugins.", ExecutionMode.Validate )
);
rootCommand.Subcommands.Add(
    CreateCommand( "check", "Dry-run the selected configuration.", ExecutionMode.Check )
);
rootCommand.Subcommands.Add(
    CreateCommand( "apply", "Apply the selected configuration.", ExecutionMode.Apply )
);

return await rootCommand.Parse( args ).InvokeAsync();

Command CreateRestoreCommand()
{
    var command = new Command( "restore", "Resolve and cache required plugins." );
    command.Options.Add( configOption );
    command.Options.Add( setOption );
    command.Options.Add( pluginCacheDirectoryOption );
    command.SetAction( async ( parseResult, cancellationToken ) =>
        {
            var configRoot = ResolveConfigRoot( parseResult );
            var cliVariables = ParseCliVariables( parseResult.GetValue( setOption ) );
            var engine = new ConfigEngine();
            var result = await engine.RestoreAsync(
                configRoot,
                cliVariables,
                stationConsole.WriteLogMessage,
                cancellationToken,
                parseResult.GetValue( pluginCacheDirectoryOption )
            );

            stationConsole.WriteRestoreResult( result );
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
    command.Options.Add( configOption );
    command.Options.Add( setOption );
    command.Options.Add( pluginCacheDirectoryOption );
    command.SetAction( async ( parseResult, cancellationToken ) =>
        {
            var configRoot = ResolveConfigRoot( parseResult );
            var cliVariables = ParseCliVariables( parseResult.GetValue( setOption ) );
            var engine = new ConfigEngine();
            var result = await engine.ExplainAsync(
                configRoot,
                cliVariables,
                stationConsole.WriteLogMessage,
                cancellationToken,
                parseResult.GetValue( pluginCacheDirectoryOption )
            );

            stationConsole.WriteExplainResult( result );
            return result.Succeeded ? 0 : 1;
        }
    );
    return command;
}

Command CreateListCommand()
{
    var command = new Command( "list", "Show available roles and modules." );
    command.Subcommands.Add(
        CreateInventoryCommand(
            "roles",
            "Show available roles, descriptions, and sources.",
            ( engine, root, variables, cacheDirectory, cancellationToken ) =>
                engine.ListRolesAsync(
                    root,
                    stationConsole.WriteLogMessage,
                    cancellationToken,
                    variables,
                    cacheDirectory
                )
        )
    );
    command.Subcommands.Add(
        CreateInventoryCommand(
            "modules",
            "Show available modules, descriptions, and sources.",
            ( engine, root, variables, cacheDirectory, cancellationToken ) =>
                engine.ListModulesAsync(
                    root,
                    stationConsole.WriteLogMessage,
                    cancellationToken,
                    variables,
                    cacheDirectory
                )
        )
    );
    return command;
}

Command CreateInventoryCommand(
    string name,
    string description,
    Func<
        ConfigEngine,
        string,
        IReadOnlyDictionary<string, string>,
        string?,
        CancellationToken,
        Task<InventoryResult>
    > list
)
{
    var command = new Command( name, description );
    command.Options.Add( configOption );
    command.Options.Add( setOption );
    command.Options.Add( pluginCacheDirectoryOption );
    command.SetAction( async ( parseResult, cancellationToken ) =>
        {
            var engine = new ConfigEngine();
            var result = await list(
                engine,
                ResolveConfigRoot( parseResult ),
                ParseCliVariables( parseResult.GetValue( setOption ) ),
                parseResult.GetValue( pluginCacheDirectoryOption ),
                cancellationToken
            );
            stationConsole.WriteInventoryResult( result, name );
            return result.Succeeded ? 0 : 1;
        }
    );
    return command;
}

Command CreateCommand( string name, string description, ExecutionMode mode )
{
    var command = new Command( name, description );
    command.Options.Add( configOption );
    command.Options.Add( setOption );
    command.Options.Add( pluginCacheDirectoryOption );
    command.SetAction( ( parseResult, cancellationToken ) =>
        ExecuteCommandAsync( parseResult, mode, cancellationToken )
    );
    return command;
}

async Task<int> ExecuteCommandAsync(
    ParseResult parseResult,
    ExecutionMode mode,
    CancellationToken cancellationToken
)
{
    var configRoot = ResolveConfigRoot( parseResult );
    var cliVariables = ParseCliVariables( parseResult.GetValue( setOption ) );

    var engine = new ConfigEngine();
    var result = await engine.RunAsync(
        configRoot,
        mode,
        cliVariables,
        stationConsole.WriteLogMessage,
        cancellationToken,
        parseResult.GetValue( pluginCacheDirectoryOption )
    );

    stationConsole.WriteResult( result );
    return result.Succeeded ? 0 : 1;
}

string ResolveConfigRoot( ParseResult parseResult )
{
    return parseResult.GetValue( configOption )!.AsLocalPath().ResolveToCurrentDirectory().Value;
}

static IReadOnlyDictionary<string, string> ParseCliVariables( string[]? values )
{
    var variables = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
    foreach ( var value in values ?? [] )
    {
        var separatorIndex = value.IndexOf( '=' );
        if ( separatorIndex <= 0 || separatorIndex == value.Length - 1 )
            throw new InvalidOperationException(
                $"Invalid '--set' value '{value}'. Expected the form key=value."
            );

        var key = value[..separatorIndex].Trim();
        var entryValue = value[(separatorIndex + 1)..].Trim();
        if ( string.IsNullOrWhiteSpace( key ) )
            throw new InvalidOperationException(
                $"Invalid '--set' value '{value}'. Variable name must not be empty."
            );

        variables[key] = entryValue;
    }

    return variables;
}