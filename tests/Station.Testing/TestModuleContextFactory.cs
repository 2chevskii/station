using Station.Abstractions.Enums;
using Station.Abstractions.Models;

namespace Station.Testing;

public static class TestModuleContextFactory
{
    public static ModuleContext Create(
        string configRoot,
        string workingDirectory,
        VariableSet? variables = null,
        ModuleArtifactSet? artifacts = null,
        TestTemplateEngine? templateEngine = null,
        ExecutionMode mode = ExecutionMode.Apply,
        bool isElevated = false,
        TimeProvider? timeProvider = null,
        string taskName = "test task",
        string? roleName = "base",
        string? roleRoot = null
    )
    {
        templateEngine ??= new TestTemplateEngine();
        variables ??= new VariableSet(new ConfigObject());
        artifacts ??= new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase)
        );

        return new ModuleContext(
            mode,
            taskName,
            configRoot,
            workingDirectory,
            roleName,
            roleRoot ?? workingDirectory,
            variables,
            artifacts,
            templateEngine,
            isElevated,
            timeProvider ?? TimeProvider.System
        );
    }
}
