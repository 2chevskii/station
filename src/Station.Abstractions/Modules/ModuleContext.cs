using Station.Abstractions.Execution;
using Station.Abstractions.Configuration;
using Station.Abstractions.Artifacts;

namespace Station.Abstractions.Modules;

public sealed class ModuleContext
{
    public ModuleContext(
        ExecutionMode mode,
        string taskName,
        string configRoot,
        string workingDirectory,
        string? roleName,
        string? roleRoot,
        VariableSet variables,
        ModuleArtifactSet artifacts,
        ITemplateEngine templateEngine,
        bool isElevated,
        TimeProvider timeProvider
    )
    {
        Mode = mode;
        TaskName = taskName;
        ConfigRoot = configRoot;
        WorkingDirectory = workingDirectory;
        RoleName = roleName;
        RoleRoot = roleRoot;
        Variables = variables;
        Artifacts = artifacts;
        TemplateEngine = templateEngine;
        IsElevated = isElevated;
        TimeProvider = timeProvider;
    }

    public ExecutionMode Mode { get; }

    public string TaskName { get; }

    public string ConfigRoot { get; }

    public string WorkingDirectory { get; }

    public string? RoleName { get; }

    public string? RoleRoot { get; }

    public VariableSet Variables { get; }

    public ModuleArtifactSet Artifacts { get; }

    public ITemplateEngine TemplateEngine { get; }

    public bool IsElevated { get; }

    public TimeProvider TimeProvider { get; }
}
