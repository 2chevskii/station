using Station.Abstractions.Execution;
using Station.Core.Execution.Running;
using Station.Output;

namespace Station.Commands;

internal abstract class EngineCommand(
    ConfigEngine engine,
    CommandOutput output,
    ExecutionMode mode
) : StationCommand( output )
{
    protected sealed override async Task<int> ExecuteCommandAsync(
        CommandInvocation invocation,
        ICommandOutput output,
        CancellationToken cancellationToken
    )
    {
        var result = await engine.RunAsync(
            invocation.ConfigRoot,
            mode,
            invocation.Variables,
            output.WriteLogMessage,
            cancellationToken,
            invocation.PluginCacheDirectory
        );
        output.WriteResult( result );
        return result.Succeeded ? 0 : 1;
    }
}
