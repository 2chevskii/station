using System.Formats.Tar;
using System.IO.Compression;
using TruePath;
using TruePath.SystemIo;

namespace Station.Testing;

public static class ArchiveBuilder
{
    public static void CreateZip(string sourceDirectory, string archivePath)
    {
        var archive = new AbsolutePath(archivePath);
        if (archive.ExistsFile())
        {
            archive.Delete();
        }

        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
    }

    public static void CreateTar(string sourceDirectory, string archivePath)
    {
        var archive = new AbsolutePath(archivePath);
        if (archive.ExistsFile())
        {
            archive.Delete();
        }

        using var stream = archive.Create();
        using var writer = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: false);

        var sourceRoot = new AbsolutePath(sourceDirectory);
        foreach (var directory in sourceRoot.GetDirectories("*", SearchOption.AllDirectories))
        {
            var relativePath = new AbsolutePath(directory).RelativeTo(sourceRoot).Value.Replace('\\', '/');
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, relativePath));
        }

        foreach (var file in sourceRoot.GetFiles("*", SearchOption.AllDirectories))
        {
            var filePath = new AbsolutePath(file);
            var relativePath = filePath.RelativeTo(sourceRoot).Value.Replace('\\', '/');
            using var fileStream = filePath.OpenRead();
            var entry = new UstarTarEntry(TarEntryType.RegularFile, relativePath)
            {
                DataStream = fileStream,
            };
            writer.WriteEntry(entry);
        }
    }
}
