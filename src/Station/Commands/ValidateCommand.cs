using Station.Abstractions.Execution;
using Station.Core.Execution.Running;
using Station.Output;

namespace Station.Commands;

internal sealed class ValidateCommand( ConfigEngine engine, CommandOutput output ) : StationCommand( output )
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandInvocation invocation,
        ICommandOutput output,
        CancellationToken cancellationToken
    )
    {
        var result = await engine.RunAsync(
            invocation.ConfigRoot,
            ExecutionMode.Validate,
            invocation.Variables,
            output.WriteLogMessage,
            cancellationToken,
            invocation.PluginCacheDirectory
        );
        output.WriteResult( result );
        return result.Succeeded ? 0 : 1;
    }
}
