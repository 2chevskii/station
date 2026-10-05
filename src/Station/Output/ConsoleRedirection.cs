namespace Station.Output;

internal sealed class ConsoleRedirection : IDisposable
{
    private readonly bool enabled;
    private readonly TextWriter previousOutput;
    private readonly TextWriter previousError;

    public ConsoleRedirection( bool enabled, TextWriter standardError )
    {
        this.enabled = enabled;
        previousOutput = Console.Out;
        previousError = Console.Error;
        if ( enabled )
        {
            Console.SetOut( standardError );
            Console.SetError( standardError );
        }
    }

    public void Dispose()
    {
        if ( enabled )
        {
            Console.SetOut( previousOutput );
            Console.SetError( previousError );
        }
    }
}
