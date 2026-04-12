using Station.Core.Infrastructure.Processes;

namespace Station.Tests.Helpers;

internal sealed class StubProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, ProcessResult> results = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> commands = new(StringComparer.OrdinalIgnoreCase);

    public List<ProcessCommand> Invocations { get; } = [];

    public bool CommandExists(string commandName) => commands.Count == 0 || commands.Contains(commandName);

    public Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        Invocations.Add(command);
        var key = CreateKey(command);
        if (results.TryGetValue(key, out var result))
        {
            return Task.FromResult(result);
        }

        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }

    public void AddCommand(string commandName) => commands.Add(commandName);

    public void SetResult(ProcessCommand command, ProcessResult result) => results[CreateKey(command)] = result;

    private static string CreateKey(ProcessCommand command) =>
        $"{command.FileName}|{string.Join('\u001f', command.Arguments)}";
}
