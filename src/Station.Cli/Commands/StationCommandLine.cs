using System.CommandLine;
using System.CommandLine.Parsing;

using Microsoft.Extensions.DependencyInjection;

using Station.Abstractions.Execution;
using Station.Cli.Output;
using Station.Core.Execution.Running;

namespace Station.Cli.Commands;

public sealed class StationCommandLine
{
    private readonly ConfigEngine engine;
    private readonly StationConsole textOutput;
    private readonly JsonCommandOutput jsonOutput;
    private readonly TextWriter standardOutput;
    private readonly TextWriter standardError;
    private readonly CommandLineOptions options = new CommandLineOptions();
    private readonly RootCommand rootCommand;

    public StationCommandLine(
        ConfigEngine engine,
        StationConsole textOutput,
        JsonCommandOutput jsonOutput,
        [FromKeyedServices( "stdout" )] TextWriter standardOutput,
        [FromKeyedServices( "stderr" )] TextWriter standardError
    )
    {
        this.engine = engine;
        this.textOutput = textOutput;
        this.jsonOutput = jsonOutput;
        this.standardOutput = standardOutput;
        this.standardError = standardError;

        rootCommand = new RootCommand( "Manage local workstation configuration." );
        options.AddTo( rootCommand );
        rootCommand.Subcommands.Add( CreateRestoreCommand() );
        rootCommand.Subcommands.Add( CreateExplainCommand() );
        var list = new Command( "list", "Show available roles and modules." );
        list.Subcommands.Add( CreateInventoryCommand( "roles" ) );
        list.Subcommands.Add( CreateInventoryCommand( "modules" ) );
        rootCommand.Subcommands.Add( list );
        rootCommand.Subcommands.Add( CreateRunCommand( "validate", ExecutionMode.Validate ) );
        rootCommand.Subcommands.Add( CreateRunCommand( "check", ExecutionMode.Check ) );
        rootCommand.Subcommands.Add( CreateRunCommand( "apply", ExecutionMode.Apply ) );
    }

    public async Task<int> RunAsync( string[] arguments, CancellationToken cancellationToken = default )
    {
        var parsed = rootCommand.Parse( arguments );
        var useJson = options.UseJson( parsed );
        var variableError = options.GetMissingVariableValueError( parsed );
        if ( variableError is not null && parsed.Action is not { ClearsParseErrors: true } )
        {
            ICommandOutput output = useJson ? jsonOutput : textOutput;
            output.WriteError(
                GetCommandName( parsed ),
                "Invalid command line.",
                parsed.Errors.Select( error => error.Message ).Append( variableError ).ToArray()
            );
            return 1;
        }

        if ( useJson )
        {
            if ( parsed.Errors.Count > 0 && parsed.Action is not { ClearsParseErrors: true } )
            {
                jsonOutput.WriteError(
                    GetCommandName( parsed ),
                    "Invalid command line.",
                    parsed.Errors.Select( error => error.Message ).ToArray()
                );
                return 1;
            }

            if ( parsed.Action is null )
            {
                jsonOutput.WriteError( GetCommandName( parsed ), "Specify a command.", [] );
                return 1;
            }
        }

        return await parsed.InvokeAsync(
            new InvocationConfiguration
            {
                Output = standardOutput,
                Error = standardError,
                EnableDefaultExceptionHandler = false,
            },
            cancellationToken
        );
    }

    private Command CreateRestoreCommand() =>
        CreateCommand(
            "restore",
            "Resolve and cache required plugins.",
            async ( invocation, output, token ) =>
            {
                var result = await engine.RestoreAsync(
                    invocation.ConfigRoot,
                    invocation.Variables,
                    output.WriteLogMessage,
                    token,
                    invocation.PluginCacheDirectory
                );
                output.WriteRestoreResult( result );
                return result.Succeeded ? 0 : 1;
            }
        );

    private Command CreateExplainCommand() =>
        CreateCommand(
            "explain",
            "Show the static task plan, bound parameters, and visible variables.",
            async ( invocation, output, token ) =>
            {
                var result = await engine.ExplainAsync(
                    invocation.ConfigRoot,
                    invocation.Variables,
                    output.WriteLogMessage,
                    token,
                    invocation.PluginCacheDirectory
                );
                output.WriteExplainResult( result );
                return result.Succeeded ? 0 : 1;
            }
        );

    private Command CreateInventoryCommand( string kind ) =>
        CreateCommand(
            kind,
            $"Show available {kind}, descriptions, and sources.",
            async ( invocation, output, token ) =>
            {
                var result = kind == "roles"
                    ? await engine.ListRolesAsync(
                        invocation.ConfigRoot,
                        output.WriteLogMessage,
                        token,
                        invocation.Variables,
                        invocation.PluginCacheDirectory
                    )
                    : await engine.ListModulesAsync(
                        invocation.ConfigRoot,
                        output.WriteLogMessage,
                        token,
                        invocation.Variables,
                        invocation.PluginCacheDirectory
                    );
                output.WriteInventoryResult( result, kind );
                return result.Succeeded ? 0 : 1;
            }
        );

    private Command CreateRunCommand( string name, ExecutionMode mode )
    {
        var description = mode switch
        {
            ExecutionMode.Validate => "Validate config and plugins.",
            ExecutionMode.Check => "Dry-run the selected configuration.",
            _ => "Apply the selected configuration.",
        };
        return CreateCommand( name, description, async ( invocation, output, token ) =>
        {
            var result = await engine.RunAsync(
                invocation.ConfigRoot,
                mode,
                invocation.Variables,
                output.WriteLogMessage,
                token,
                invocation.PluginCacheDirectory
            );
            output.WriteResult( result );
            return result.Succeeded ? 0 : 1;
        } );
    }

    private Command CreateCommand(
        string name,
        string description,
        Func<CommandInvocation, ICommandOutput, CancellationToken, Task<int>> action
    )
    {
        var command = new Command( name, description );
        command.SetAction( ( parsed, token ) => ExecuteAsync( parsed, token, action ) );
        return command;
    }

    private async Task<int> ExecuteAsync(
        ParseResult parsed,
        CancellationToken cancellationToken,
        Func<CommandInvocation, ICommandOutput, CancellationToken, Task<int>> action
    )
    {
        var useJson = options.UseJson( parsed );
        ICommandOutput output = useJson ? jsonOutput : textOutput;
        var previousOutput = Console.Out;
        var previousError = Console.Error;
        if ( useJson )
        {
            Console.SetOut( standardError );
            Console.SetError( standardError );
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action( options.Read( parsed ), output, cancellationToken );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            output.WriteError( GetCommandName( parsed ), "Operation cancelled.", [], 130 );
            return 130;
        }
        catch ( Exception exception )
        {
            output.WriteError( GetCommandName( parsed ), exception.Message, [exception.Message] );
            return 1;
        }
        finally
        {
            if ( useJson )
            {
                Console.SetOut( previousOutput );
                Console.SetError( previousError );
            }
        }
    }

    private static string GetCommandName( ParseResult parsed )
    {
        var names = new List<string>();
        CommandResult? current = parsed.CommandResult;
        while ( current is not null && current.Command is not RootCommand )
        {
            names.Add( current.Command.Name );
            current = current.Parent as CommandResult;
        }

        names.Reverse();
        return names.Count == 0 ? "station" : string.Join( ' ', names );
    }
}
