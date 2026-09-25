using Station.Abstractions.FileSystem;

using TruePath.SystemIo;

namespace Station.Abstractions.Artifacts;

public sealed class ManagedFile
{
    public ManagedFile( string name, string sourcePath )
    {
        Name = name;
        SourcePath = sourcePath;
    }

    public string Name { get; }

    public string SourcePath { get; }

    public byte[] ReadAllBytes() => SourcePath.AsAbsolutePath().ReadAllBytes();

    public string ReadAllText() => SourcePath.AsAbsolutePath().ReadAllText();
}