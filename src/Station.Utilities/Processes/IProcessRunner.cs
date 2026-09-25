namespace Station.Utilities.Processes;

public interface IProcessRunner
{
    bool CommandExists(string commandName);

    Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken);
}
