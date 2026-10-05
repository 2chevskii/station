using Station.Core.Execution.Running;
using Station.Output;

namespace Station.Commands;

internal sealed class ExplainCommand( ConfigEngine engine, CommandOutput output ) : StationCommand( output )
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandInvocation invocation,
        ICommandOutput output,
        CancellationToken cancellationToken
    )
    {
        var result = await engine.ExplainAsync(
            invocation.ConfigRoot,
            invocation.Variables,
            output.WriteLogMessage,
            cancellationToken,
            invocation.PluginCacheDirectory
        );
        output.WriteExplainResult( result );
        return result.Succeeded ? 0 : 1;
    }
}
