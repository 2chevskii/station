using Station.Abstractions.Execution;
using Station.Core.Configuration.Loading;

namespace Station.Core.Execution.Modules;

internal sealed class ModuleContextFactory(
    ITemplateEngine templateEngine,
    TimeProvider timeProvider
)
{
    public ModuleContext Create(
        string configRoot,
        LoadedTask task,
        ExecutionMode mode,
        bool isElevated
    ) =>
        new ModuleContext(
            mode,
            task.Name,
            configRoot,
            task.TaskRoot,
            task.RoleName,
            task.RoleRoot,
            task.Variables,
            ModuleArtifactFactory.Create( configRoot, task.RoleRoot, task.Variables, templateEngine ),
            templateEngine,
            isElevated,
            timeProvider
        );
}
