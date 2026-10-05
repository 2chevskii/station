using TruePath;
using TruePath.SystemIo;

namespace Station.Modules.Builtin.Dotfile;

internal static class AtomicFileWriter
{
    private const int UnableToMoveReplacementAfterBackup = 1177;

    public static async Task WriteAsync(
        AbsolutePath destination,
        ReadOnlyMemory<byte> contents,
        bool backup,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = HostFilePaths.ResolveTarget( destination );
        var existed = target.ExistsFile();
        if ( existed && (File.GetAttributes( target.Value ) & FileAttributes.ReadOnly) != 0 )
            throw new UnauthorizedAccessException( $"Destination '{destination}' is read-only." );

        var parent = target.Parent
            ?? throw new IOException( $"Destination '{destination}' has no parent directory." );
        parent.CreateDirectory();
        var staged = parent / $".station-{Guid.NewGuid():N}.tmp";
        var ownsStagedFile = false;
        var committed = false;
        string? backupPath = null;

        try
        {
            using ( var reservation = FilePermissions.CreateNew(
                staged.Value,
                existed ? target.Value : null
            ) )
                ownsStagedFile = true;

            if ( existed )
            {
                FilePermissions.Copy( target.Value, staged.Value );
                File.Copy( target.Value, staged.Value, overwrite: true );
                FilePermissions.Copy( target.Value, staged.Value );
            }

            await using ( var stream = new FileStream( staged.Value, new FileStreamOptions
            {
                Mode = FileMode.Truncate,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            } ) )
            {
                await stream.WriteAsync( contents, cancellationToken );
                await stream.FlushAsync( cancellationToken );
            }

            cancellationToken.ThrowIfCancellationRequested();
            if ( backup && existed )
                backupPath = FileBackups.Create( target, destination, timeProvider, cancellationToken );

            cancellationToken.ThrowIfCancellationRequested();
            if ( existed )
                ReplaceExisting( staged, target, cancellationToken );
            else
                File.Move( staged.Value, target.Value );

            committed = true;
        }
        finally
        {
            if ( ownsStagedFile )
                File.Delete( staged.Value );

            if ( !committed && backupPath is not null )
                File.Delete( backupPath );
        }
    }

    private static void ReplaceExisting(
        AbsolutePath staged,
        AbsolutePath target,
        CancellationToken cancellationToken
    )
    {
        var recovery = target.Parent!.Value / $".station-{Guid.NewGuid():N}.recovery";
        using ( var reservation = FilePermissions.CreateNew( recovery.Value, target.Value ) )
        {
        }

        var preserveRecovery = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Replace( staged.Value, target.Value, recovery.Value );
        }
        catch ( IOException exception ) when (
            OperatingSystem.IsWindows()
            && (exception.HResult & 0xffff) == UnableToMoveReplacementAfterBackup
        )
        {
            // ReplaceFile error 1177 leaves the original at the supplied recovery path.
            try
            {
                File.Move( recovery.Value, target.Value );
            }
            catch ( Exception recoveryError ) when (
                recoveryError is IOException or UnauthorizedAccessException
            )
            {
                preserveRecovery = true;
                throw new IOException(
                    $"File replacement failed. Previous contents are preserved at '{recovery}'.",
                    new AggregateException( exception, recoveryError )
                );
            }

            throw;
        }
        finally
        {
            if ( !preserveRecovery )
                File.Delete( recovery.Value );
        }
    }
}
