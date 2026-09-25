using System.Formats.Tar;
using System.IO.Compression;

using Station.Abstractions.FileSystem;
using TruePath;
using TruePath.SystemIo;

namespace Station.Testing;

public static class ArchiveBuilder
{
    public static void CreateZip(string sourceDirectory, string archivePath)
    {
        var archive = archivePath.AsAbsolutePath();
        if (archive.ExistsFile())
        {
            archive.Delete();
        }

        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
    }

    public static void CreateTar(string sourceDirectory, string archivePath)
    {
        var archive = archivePath.AsAbsolutePath();
        if (archive.ExistsFile())
        {
            archive.Delete();
        }

        using var stream = archive.Create();
        using var writer = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: false);

        var sourceRoot = sourceDirectory.AsAbsolutePath();
        foreach (var directory in sourceRoot.GetDirectories("*", SearchOption.AllDirectories))
        {
            var relativePath = directory.AsAbsolutePath().RelativeTo(sourceRoot).Value.Replace('\\', '/');
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, relativePath));
        }

        foreach (var file in sourceRoot.GetFiles("*", SearchOption.AllDirectories))
        {
            var filePath = file.AsAbsolutePath();
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
