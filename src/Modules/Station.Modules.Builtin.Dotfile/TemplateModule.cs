using System.Text;
using Station.Abstractions.Attributes;
using Station.Abstractions.Enums;
using Station.Abstractions.Models;

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
            var privilege = RequiresAdmin(Destination)
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
            var destinationPath = ResolveHostPath(Destination);
            var desiredBytes = Utf8WithoutBom.GetBytes(template.GetRenderedText());
            var existingBytes = File.Exists(destinationPath)
                ? await File.ReadAllBytesAsync(destinationPath, cancellationToken)
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
        string destinationPath,
        byte[] desiredBytes,
        CancellationToken cancellationToken
    )
    {
        var parentDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        if (File.Exists(destinationPath) && Backup)
        {
            var timestamp = Context.TimeProvider.GetUtcNow().ToString("yyyyMMddHHmmss");
            File.Copy(destinationPath, $"{destinationPath}.bak.{timestamp}", overwrite: true);
        }

        await File.WriteAllBytesAsync(destinationPath, desiredBytes, cancellationToken);
    }

    private static string ResolveHostPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(expanded, Environment.CurrentDirectory);
    }

    private static bool RequiresAdmin(string destination)
    {
        var resolved = ResolveHostPath(destination);
        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        }.Where(static path => !string.IsNullOrWhiteSpace(path));

        return protectedRoots.Any(root =>
            resolved.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
        );
    }
}
