using System.Formats.Tar;
using System.IO.Compression;

namespace Station.Testing;

public static class ArchiveBuilder
{
    public static void CreateZip(string sourceDirectory, string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
    }

    public static void CreateTar(string sourceDirectory, string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using var stream = File.Create(archivePath);
        using var writer = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: false);

        foreach (
            var directory in Directory.GetDirectories(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory).Replace('\\', '/');
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            using var fileStream = File.OpenRead(file);
            var entry = new UstarTarEntry(TarEntryType.RegularFile, relativePath)
            {
                DataStream = fileStream,
            };
            writer.WriteEntry(entry);
        }
    }
}
