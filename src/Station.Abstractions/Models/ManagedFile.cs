using TruePath.SystemIo;
using TruePath;

namespace Station.Abstractions.Models;

public sealed class ManagedFile
{
    public ManagedFile(string name, string sourcePath)
    {
        Name = name;
        SourcePath = sourcePath;
    }

    public string Name { get; }

    public string SourcePath { get; }

    public byte[] ReadAllBytes() => new AbsolutePath(SourcePath).ReadAllBytes();

    public string ReadAllText() => new AbsolutePath(SourcePath).ReadAllText();
}
