using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Station.Commands;

namespace Station.Output;

internal sealed class CommandOutputSession : IDisposable
{
    private readonly CommandArguments invocation;
    private readonly TextWriter standardOutput;
    private readonly TextWriter standardError;
    private readonly StringWriter helpOutput = new StringWriter();
    private readonly IAnsiConsole standardConsole;
    private readonly IAnsiConsole errorConsole;
    private readonly StationConsole textOutput;
    private readonly JsonCommandOutput jsonOutput;
    private readonly ICommandOutput output;

    public IAnsiConsole HelpConsole { get; }

    public CommandOutputSession(
        CommandArguments invocation,
        TextWriter standardOutput,
        TextWriter standardError
    )
    {
        this.invocation = invocation;
        this.standardOutput = standardOutput;
        this.standardError = standardError;
        standardConsole = CreateConsole( standardOutput );
        errorConsole = CreateConsole( standardError );
        textOutput = new StationConsole( standardConsole, errorConsole );
        jsonOutput = new JsonCommandOutput( standardOutput, standardError );
        output = invocation.UseJson ? jsonOutput : textOutput;
        HelpConsole = CreateConsole( helpOutput );
    }

    public void RegisterServices( IServiceCollection services )
    {
        services.AddKeyedSingleton( "stdout", standardOutput );
        services.AddKeyedSingleton( "stderr", standardError );
        services.AddKeyedSingleton( "stdout", standardConsole );
        services.AddKeyedSingleton( "stderr", errorConsole );
        services.AddSingleton( textOutput );
        services.AddSingleton( jsonOutput );
        services.AddSingleton<CommandOutput>();
    }

    public bool ValidateArguments()
    {
        if ( invocation.Error is not { } error || invocation.ShowHelp )
            return true;

        output.WriteError( invocation.CommandName, "Invalid command line.", [error] );
        return false;
    }

    public int HandleException( Exception exception, CancellationToken cancellationToken )
    {
        if ( exception is OperationCanceledException && cancellationToken.IsCancellationRequested )
        {
            output.WriteError( invocation.CommandName, "Operation cancelled.", [], 130 );
            return 130;
        }

        output.WriteError( invocation.CommandName, exception.Message, [exception.Message] );
        return 1;
    }

    public IDisposable RedirectConsole() =>
        new ConsoleRedirection( invocation.UseJson && !invocation.ShowHelp, standardError );

    public async Task<int> WriteHelpAsync( int exitCode )
    {
        if ( helpOutput.GetStringBuilder().Length == 0 )
            return exitCode;

        if ( invocation.UseJson && !invocation.ShowHelp )
        {
            output.WriteError( invocation.CommandName, "Specify a command.", [] );
            return 1;
        }

        await standardOutput.WriteAsync( helpOutput.ToString() );
        return exitCode;
    }

    public void Dispose() => helpOutput.Dispose();

    private static IAnsiConsole CreateConsole( TextWriter writer ) =>
        AnsiConsole.Create( new AnsiConsoleSettings { Out = new AnsiConsoleOutput( writer ) } );
}
