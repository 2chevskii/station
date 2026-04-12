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

    public byte[] ReadAllBytes() => File.ReadAllBytes(SourcePath);

    public string ReadAllText() => File.ReadAllText(SourcePath);
}
