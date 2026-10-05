using System.Text;

namespace Station.Abstractions.Processes;

public sealed record ProcessCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    Encoding? StandardOutputEncoding = null,
    Encoding? StandardErrorEncoding = null
)
{
    public override string ToString() => $"{FileName} {string.Join( ' ', Arguments )}";
}
