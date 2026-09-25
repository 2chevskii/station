using Station.Abstractions.Modules;
using Station.Abstractions.Execution;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;

using TruePath;
using TruePath.SystemIo;

namespace Station.Modules.Builtin.Dotfile;

[Module("copy", "Copies a file from the module artifact tree to the local machine.")]
public sealed class CopyModule : ConfigModule
{
    public CopyModule(ModuleContext context)
        : base(context) { }

    [Parameter("file", Required = true)]
    public string FileName { get; set; } = string.Empty;

    [Parameter(Required = true)]
    public string Destination { get; set; } = string.Empty;

    [Parameter]
    public bool Backup { get; set; } = true;

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            _ = Context.Artifacts.GetRequiredFile(FileName);
            var privilege = HostFilePaths.RequiresAdmin(Destination)
                ? PrivilegeRequirement.Administrator
                : PrivilegeRequirement.User;
            return ValueTask.FromResult(ModuleInspectionResult.Success(privilege));
        }
        catch (ConfigurationException exception)
        {
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure(new ModuleDiagnostic(exception.Message))
            );
        }
    }

    public override async ValueTask<ModuleResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sourceFile = Context.Artifacts.GetRequiredFile(FileName);
            var destinationPath = HostFilePaths.Resolve(Destination);
            var desiredBytes = sourceFile.ReadAllBytes();
            var existingBytes = destinationPath.ExistsFile()
                ? await destinationPath.ReadAllBytesAsync(cancellationToken)
                : null;
            var changed = existingBytes is null || !existingBytes.SequenceEqual(desiredBytes);

            if (Context.Mode == ExecutionMode.Check)
            {
                return changed
                    ? ModuleResult.ChangedResult($"File '{destinationPath}' would change.")
                    : ModuleResult.NoChange(
                        $"File '{destinationPath}' is already in the desired state."
                    );
            }

            if (!changed)
            {
                return ModuleResult.NoChange(
                    $"File '{destinationPath}' is already in the desired state."
                );
            }

            await WriteBytesAsync(destinationPath, desiredBytes, cancellationToken);
            return ModuleResult.ChangedResult($"File '{destinationPath}' was updated.");
        }
        catch (ConfigurationException exception)
        {
            return ModuleResult.Failure(exception.Message);
        }
    }

    private async Task WriteBytesAsync(
        AbsolutePath destinationPath,
        byte[] desiredBytes,
        CancellationToken cancellationToken
    )
    {
        if (destinationPath.Parent is { } parentDirectory)
        {
            parentDirectory.CreateDirectory();
        }

        if (destinationPath.ExistsFile() && Backup)
        {
            var timestamp = Context.TimeProvider.GetUtcNow().ToString("yyyyMMddHHmmss");
            destinationPath.Copy(
                $"{destinationPath}.bak.{timestamp}".AsAbsolutePath(),
                overwrite: true
            );
        }

        await destinationPath.WriteAllBytesAsync(desiredBytes, cancellationToken);
    }
}
