using Station.Abstractions.Processes;

namespace Station.Testing;

public sealed class RecordingProcessRunner( params ProcessResult[] results ) : IProcessRunner
{
    private readonly Queue<ProcessResult> results = new Queue<ProcessResult>( results );

    public List<ProcessCommand> Commands { get; } = [];

    public bool CommandExists( string commandName ) => true;

    public Task<ProcessResult> RunAsync( ProcessCommand command, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add( command );
        if ( !results.TryDequeue( out var result ) )
            throw new InvalidOperationException( $"Unexpected process command: {command}" );

        return Task.FromResult( result );
    }
}
