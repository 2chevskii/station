using SharpCompress.Archives;
using SharpCompress.Common;

namespace Station.Plugins.Restoration;

internal static class PluginArchiveExtractor
{
    public static void Extract( string archivePath, string destinationRoot )
    {
        using var archive = ArchiveFactory.OpenArchive( archivePath );
        foreach ( var entry in archive.Entries.Where( static entry => !entry.IsDirectory ) )
            entry.WriteToDirectory(
                destinationRoot,
                new ExtractionOptions { ExtractFullPath = true, Overwrite = true }
            );
    }
}
