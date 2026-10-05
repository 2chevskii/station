using Station.Core.Execution.Running;
using Station.Output;

namespace Station.Commands;

internal sealed class ListModulesCommand( ConfigEngine engine, CommandOutput output ) : StationCommand( output )
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandInvocation invocation,
        ICommandOutput output,
        CancellationToken cancellationToken
    )
    {
        var result = await engine.ListModulesAsync(
            invocation.ConfigRoot,
            output.WriteLogMessage,
            cancellationToken,
            invocation.Variables,
            invocation.PluginCacheDirectory
        );
        output.WriteInventoryResult( result, "modules" );
        return result.Succeeded ? 0 : 1;
    }
}
