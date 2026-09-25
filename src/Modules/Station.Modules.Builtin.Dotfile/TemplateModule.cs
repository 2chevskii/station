using System.Text;
using Station.Abstractions.Modules;
using Station.Abstractions.Execution;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Configuration;

using TruePath;
using TruePath.SystemIo;

namespace Station.Modules.Builtin.Dotfile;

[Module("template", "Renders a template from the module artifact tree to the local machine.")]
public sealed class TemplateModule : ConfigModule
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public TemplateModule(ModuleContext context)
        : base(context) { }

    [Parameter("template", Required = true)]
    public string TemplateName { get; set; } = string.Empty;

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
            _ = Context.Artifacts.GetRequiredTemplate(TemplateName);
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
            var template = Context.Artifacts.GetRequiredTemplate(TemplateName);
            var destinationPath = HostFilePaths.Resolve(Destination);
            var desiredBytes = Utf8WithoutBom.GetBytes(template.GetRenderedText());
            var existingBytes = destinationPath.ExistsFile()
                ? await destinationPath.ReadAllBytesAsync(cancellationToken)
                : null;
            var changed = existingBytes is null || !existingBytes.SequenceEqual(desiredBytes);

            if (Context.Mode == ExecutionMode.Check)
            {
                return changed
                    ? ModuleResult.ChangedResult($"Template '{destinationPath}' would change.")
                    : ModuleResult.NoChange(
                        $"Template '{destinationPath}' is already in the desired state."
                    );
            }

            if (!changed)
            {
                return ModuleResult.NoChange(
                    $"Template '{destinationPath}' is already in the desired state."
                );
            }

            await WriteBytesAsync(destinationPath, desiredBytes, cancellationToken);
            return ModuleResult.ChangedResult($"Template '{destinationPath}' was updated.");
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
