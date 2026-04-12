namespace Station.Testing;

public sealed class CurrentDirectoryScope : IDisposable
{
    private readonly string originalDirectory;

    public CurrentDirectoryScope(string directory)
    {
        originalDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = directory;
    }

    public void Dispose() => Environment.CurrentDirectory = originalDirectory;
}
