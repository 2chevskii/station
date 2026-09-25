using Station.Abstractions.FileSystem;
using TruePath;
using TruePath.SystemIo;

namespace Station.Testing;

public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = (
            Temporary.SystemTempDirectory() / "station-tests" / Guid.NewGuid().ToString("N")
        ).Value;
        Path.AsAbsolutePath().CreateDirectory();
    }

    public string Path { get; }

    public string GetPath(params string[] segments)
    {
        var path = Path.AsAbsolutePath();
        foreach (var segment in segments)
        {
            path /= segment;
        }

        return path.Value;
    }

    public void WriteFile(string relativePath, string contents)
    {
        var fullPath = GetPath(relativePath).AsAbsolutePath();
        if (fullPath.Parent is { } directory)
        {
            directory.CreateDirectory();
        }

        fullPath.WriteAllText(contents.Replace("\r\n", "\n"));
    }

    public void Dispose()
    {
        var root = Path.AsAbsolutePath();
        if (root.ExistsDirectory())
        {
            ClearAttributes(root);
            root.DeleteDirectoryRecursively();
        }
    }

    private static void ClearAttributes(AbsolutePath root)
    {
        foreach (var directory in root.GetDirectories("*", SearchOption.AllDirectories))
        {
            directory.AsAbsolutePath().SetAttributes(FileAttributes.Normal);
        }

        foreach (var file in root.GetFiles("*", SearchOption.AllDirectories))
        {
            file.AsAbsolutePath().SetAttributes(FileAttributes.Normal);
        }

        root.SetAttributes(FileAttributes.Normal);
    }
}
