using Station.Abstractions.Execution;
using Station.Core.Execution.Running;

namespace Station.Commands;

internal sealed class CheckCommand( ConfigEngine engine, CommandOutput output )
    : EngineCommand( engine, output, ExecutionMode.Check )
{
}
