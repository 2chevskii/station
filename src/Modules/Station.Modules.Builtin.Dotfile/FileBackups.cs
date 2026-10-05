using System.Globalization;

using TruePath;

namespace Station.Modules.Builtin.Dotfile;

internal static class FileBackups
{
    public static string Create(
        AbsolutePath source,
        AbsolutePath destination,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        var timestamp = timeProvider.GetUtcNow().ToString(
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture
        );
        var prefix = $"{destination.Value}.bak.{timestamp}";
        for ( var suffix = 0; ; suffix++ )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = suffix == 0
                ? prefix
                : $"{prefix}.{suffix.ToString( CultureInfo.InvariantCulture )}";
            if ( EntryExists( path ) )
                continue;

            try
            {
                using var reservation = FilePermissions.CreateNew( path, source.Value );
            }
            catch ( IOException ) when ( EntryExists( path ) )
            {
                continue;
            }

            try
            {
                // This path was reserved by this invocation; existing backups are never overwritten.
                FilePermissions.Copy( source.Value, path );
                File.Copy( source.Value, path, overwrite: true );
                FilePermissions.Copy( source.Value, path );
                cancellationToken.ThrowIfCancellationRequested();
                return path;
            }
            catch
            {
                File.Delete( path );
                throw;
            }
        }
    }

    private static bool EntryExists( string path ) =>
        File.Exists( path ) || Directory.Exists( path ) || new FileInfo( path ).LinkTarget is not null;
}
