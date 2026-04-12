using System.Text;
using System.Text.RegularExpressions;
using Station.Abstractions;
using Fluid;

namespace Station.Modules.Dotfile;

public sealed class DotfileModule : IConfigModule
{
    private static readonly Regex PlaceholderRegex = new(@"\$\{(?<name>[A-Za-z0-9_.-]+)\}", RegexOptions.Compiled);
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly FluidParser parser = new();
    private readonly TimeProvider timeProvider;

    public DotfileModule()
        : this(TimeProvider.System)
    {
    }

    public DotfileModule(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public ModuleDescriptor Descriptor { get; } =
        new("dotfile", "1.0.0", "Copies or renders files from the task root to the local machine.");

    public ValueTask<ModuleInspectionResult> InspectAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourcePath = ResolveSourcePath(request);
            if (!File.Exists(sourcePath))
            {
                return ValueTask.FromResult(
                    ModuleInspectionResult.Failure(
                        new ModuleDiagnostic($"Source file '{sourcePath}' does not exist.")));
            }

            var destination = request.Parameters.RequireString("destination");
            var privilege = RequiresAdmin(destination)
                ? PrivilegeRequirement.Administrator
                : PrivilegeRequirement.User;
            return ValueTask.FromResult(ModuleInspectionResult.Success(privilege));
        }
        catch (ConfigurationException exception)
        {
            return ValueTask.FromResult(ModuleInspectionResult.Failure(new ModuleDiagnostic(exception.Message)));
        }
    }

    public async ValueTask<ModuleResult> ExecuteAsync(
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourcePath = ResolveSourcePath(request);
            var destinationPath = ResolveHostPath(request.Parameters.RequireString("destination"));

            var desiredContent = await GetDesiredContentAsync(sourcePath, request, cancellationToken);
            var desiredBytes = Utf8WithoutBom.GetBytes(desiredContent);
            var existingBytes = File.Exists(destinationPath)
                ? await File.ReadAllBytesAsync(destinationPath, cancellationToken)
                : null;
            var changed = existingBytes is null || !existingBytes.SequenceEqual(desiredBytes);

            if (request.Mode == ExecutionMode.Check)
            {
                return changed
                    ? ModuleResult.ChangedResult($"Dotfile '{destinationPath}' would change.")
                    : ModuleResult.NoChange($"Dotfile '{destinationPath}' is already in the desired state.");
            }

            if (!changed)
            {
                return ModuleResult.NoChange($"Dotfile '{destinationPath}' is already in the desired state.");
            }

            var parentDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            if (File.Exists(destinationPath) && request.Parameters.GetBoolean("backup", true))
            {
                var timestamp = timeProvider.GetUtcNow().ToString("yyyyMMddHHmmss");
                File.Copy(destinationPath, $"{destinationPath}.bak.{timestamp}", overwrite: true);
            }

            await File.WriteAllBytesAsync(destinationPath, desiredBytes, cancellationToken);
            return ModuleResult.ChangedResult($"Dotfile '{destinationPath}' was updated.");
        }
        catch (ConfigurationException exception)
        {
            return ModuleResult.Failure(exception.Message);
        }
    }

    private async Task<string> GetDesiredContentAsync(
        string sourcePath,
        ModuleExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var sourceText = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var isTemplate = request.Parameters.GetBoolean("template") ||
            sourcePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains("templates", StringComparer.OrdinalIgnoreCase);

        return isTemplate
            ? RenderTemplate(sourceText, request.Variables, sourcePath)
            : sourceText;
    }

    private string ResolveSourcePath(ModuleExecutionRequest request)
    {
        var relativeSource = request.Parameters.RequireString("source");
        var sourcePath = Path.GetFullPath(relativeSource, request.TaskRoot);
        var taskRoot = Path.GetFullPath(request.TaskRoot);

        if (!sourcePath.StartsWith(taskRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException("Dotfile source must stay within the task root.");
        }

        return sourcePath;
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
            resolved.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
    }

    private string RenderTemplate(string template, IReadOnlyDictionary<string, string> variables, string sourcePath)
    {
        var normalizedTemplate = PlaceholderRegex.Replace(template, static match => $"{{{{ {match.Groups["name"].Value} }}}}");
        if (!parser.TryParse(normalizedTemplate, out var parsedTemplate, out var error))
        {
            throw new ConfigurationException($"Failed to parse template '{sourcePath}': {error}");
        }

        var context = new TemplateContext(new TemplateOptions());
        foreach (var (key, value) in variables)
        {
            context.SetValue(key, value);
        }

        return parsedTemplate.Render(context);
    }
}
