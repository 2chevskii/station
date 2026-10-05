using Spectre.Console.Cli;

namespace Station.Commands;

internal sealed class RootCommand( CommandOutput output ) : Command<StationSettings>
{
    public override int Execute(
        CommandContext context,
        StationSettings settings,
        CancellationToken cancellationToken
    )
    {
        output.Select( settings.UseJson ).WriteError( "station", "Specify a command.", [] );
        return 1;
    }
}
