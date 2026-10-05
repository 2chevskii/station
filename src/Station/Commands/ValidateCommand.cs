using Station.Abstractions.Execution;
using Station.Core.Execution.Running;

namespace Station.Commands;

internal sealed class ValidateCommand( ConfigEngine engine, CommandOutput output )
    : EngineCommand( engine, output, ExecutionMode.Validate )
{
}
