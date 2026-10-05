using Microsoft.Extensions.DependencyInjection;

using Spectre.Console.Cli;

using Station.Commands;
using Station.Output;

namespace Station;

public static class Program
{
    public static Task<int> Main( string[] args ) => RunAsync( args );

    public static async Task<int> RunAsync(
        string[] arguments,
        Action<IServiceCollection>? configureServices = null,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default
    )
    {
        var invocation = new CommandArguments( arguments );
        using var output = new CommandOutputSession(
            invocation, standardOutput ?? Console.Out, standardError ?? Console.Error
        );
        if ( !output.ValidateArguments() )
            return 1;

        var services = new ServiceCollection().AddStation();
        output.RegisterServices( services );
        configureServices?.Invoke( services );
        await using var registrar = new TypeRegistrar( services );
        var app = new CommandApp( registrar );
        app.SetDefaultCommand<RootCommand>();
        app.Configure( StationCommands.Configure );
        app.Configure( configuration =>
        {
            configuration.ConfigureConsole( output.HelpConsole );
            configuration.SetExceptionHandler( ( exception, _ ) =>
                output.HandleException( exception, cancellationToken ) );
        } );

        using var redirection = output.RedirectConsole();
        var exitCode = await app.RunAsync( invocation.Values, cancellationToken );
        return await output.WriteHelpAsync( exitCode );
    }
}
