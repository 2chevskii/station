namespace Station.Testing;

public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "station-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(params string[] segments) =>
        System.IO.Path.Combine([Path, .. segments]);

    public void WriteFile(string relativePath, string contents)
    {
        var fullPath = GetPath(relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents.Replace("\r\n", "\n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            ClearAttributes(Path);
            Directory.Delete(Path, recursive: true);
        }
    }

    private static void ClearAttributes(string root)
    {
        foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        File.SetAttributes(root, FileAttributes.Normal);
    }
}
