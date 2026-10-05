using Station.Output;

namespace Station.Commands;

internal sealed class CommandOutput( StationConsole textOutput, JsonCommandOutput jsonOutput )
{
    public ICommandOutput Select( bool useJson ) => useJson ? jsonOutput : textOutput;
}
