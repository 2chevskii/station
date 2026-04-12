namespace Station.Core.Configuration.Models;

public sealed class DiscoveredDirectory
{
    public DiscoveredDirectory(
        string fullPath,
        string name,
        string relativePath,
        IReadOnlyList<DiscoveredDirectory> directories,
        IReadOnlyList<DiscoveredFile> files)
    {
        FullPath = fullPath;
        Name = name;
        RelativePath = relativePath;
        Directories = directories;
        Files = files;
    }

    public string FullPath { get; }

    public string Name { get; }

    public string RelativePath { get; }

    public IReadOnlyList<DiscoveredDirectory> Directories { get; }

    public IReadOnlyList<DiscoveredFile> Files { get; }

    public bool TryGetDirectory(string name, out DiscoveredDirectory? directory)
    {
        directory = Directories.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return directory is not null;
    }

    public bool TryGetFile(string name, out DiscoveredFile? file)
    {
        file = Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return file is not null;
    }
}
