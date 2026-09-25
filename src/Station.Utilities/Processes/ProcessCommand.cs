namespace Station.Utilities.Processes;

public sealed record ProcessCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null
)
{
    public override string ToString() => $"{FileName} {string.Join( ' ', Arguments )}";
}