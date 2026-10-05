using Station.Core.Execution.Inventory;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Core.Plugins.Restoration;

namespace Station.Cli.Output;

public interface ICommandOutput
{
    void WriteLogMessage( string message );
    void WriteResult( EngineRunResult result );
    void WriteRestoreResult( PluginRestoreResult result );
    void WriteExplainResult( ExplainResult result );
    void WriteInventoryResult( InventoryResult result, string kind );
    void WriteError(
        string command,
        string message,
        IReadOnlyList<string> diagnostics,
        int exitCode = 1
    );
}
