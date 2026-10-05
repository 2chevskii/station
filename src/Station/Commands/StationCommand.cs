using Spectre.Console.Cli;

using Station.Output;

namespace Station.Commands;

internal abstract class StationCommand( CommandOutput output ) : AsyncCommand<StationSettings>
{
    public sealed override Task<int> ExecuteAsync(
        CommandContext context,
        StationSettings settings,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteCommandAsync(
            settings.ToInvocation(),
            output.Select( settings.UseJson ),
            cancellationToken
        );
    }

    protected abstract Task<int> ExecuteCommandAsync(
        CommandInvocation invocation,
        ICommandOutput output,
        CancellationToken cancellationToken
    );
}
