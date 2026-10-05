using Station.Abstractions.Execution;
using Station.Abstractions.Modules;

using TruePath;
using TruePath.SystemIo;

namespace Station.Modules.Builtin.Dotfile;

internal static class FileDeployment
{
    public static async ValueTask<ModuleResult> ApplyAsync(
        AbsolutePath destinationPath,
        AbsolutePath targetPath,
        byte[] desiredBytes,
        bool backup,
        ModuleContext context,
        string artifactKind,
        CancellationToken cancellationToken
    )
    {
        var existingBytes = targetPath.ExistsFile()
            ? await targetPath.ReadAllBytesAsync( cancellationToken )
            : null;
        var changed = existingBytes is null || !existingBytes.SequenceEqual( desiredBytes );

        if ( context.Mode == ExecutionMode.Check )
            return changed
                ? ModuleResult.ChangedResult( $"{artifactKind} '{destinationPath}' would change." )
                : ModuleResult.NoChange(
                    $"{artifactKind} '{destinationPath}' is already in the desired state."
                );

        if ( !changed )
            return ModuleResult.NoChange(
                $"{artifactKind} '{destinationPath}' is already in the desired state."
            );

        await AtomicFileWriter.WriteAsync(
            destinationPath,
            desiredBytes,
            backup,
            context.TimeProvider,
            cancellationToken
        );
        return ModuleResult.ChangedResult( $"{artifactKind} '{destinationPath}' was updated." );
    }
}
