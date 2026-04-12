using System.Text.Json.Nodes;
using Station.Abstractions.Enums;
using Station.Abstractions.Models;
using Station.Core.Execution;
using Station.Core.Execution.Services;
using Station.Core.Infrastructure.Templates;
using Station.Modules.Builtin.Dotfile;
using Station.Tests.Helpers;

namespace Station.Tests.Modules;

public sealed class TemplateModuleTests
{
    [Fact]
    public async Task Execute_is_idempotent_for_template_module()
    {
        using var directory = new TemporaryDirectory();
        var taskRoot = Path.Combine(directory.Path, "role");
        Directory.CreateDirectory(Path.Combine(taskRoot, "templates"));
        var templatePath = Path.Combine(taskRoot, "templates", "app.scriban");
        await File.WriteAllTextAsync(templatePath, "hello {{ name }}");

        var destinationPath = Path.Combine(directory.Path, "out.txt");
        var variables = new VariableSet(new JsonObject { ["name"] = "Alice" });
        var templateEngine = new ScribanTemplateRenderer();

        var checkContext = new ModuleContext(
            ExecutionMode.Check,
            "render app",
            directory.Path,
            taskRoot,
            "base",
            taskRoot,
            variables,
            ModuleArtifactFactory.Create(directory.Path, taskRoot, variables, templateEngine),
            templateEngine,
            isElevated: false,
            TimeProvider.System
        );
        var applyContext = new ModuleContext(
            ExecutionMode.Apply,
            "render app",
            directory.Path,
            taskRoot,
            "base",
            taskRoot,
            variables,
            ModuleArtifactFactory.Create(directory.Path, taskRoot, variables, templateEngine),
            templateEngine,
            isElevated: false,
            TimeProvider.System
        );

        var checkModule = new TemplateModule(checkContext)
        {
            TemplateName = "app",
            Destination = destinationPath,
        };
        var applyModule = new TemplateModule(applyContext)
        {
            TemplateName = "app",
            Destination = destinationPath,
        };

        var initialCheck = await checkModule.ExecuteAsync(CancellationToken.None);
        Assert.True(initialCheck.Changed);

        var applyResult = await applyModule.ExecuteAsync(CancellationToken.None);
        Assert.True(applyResult.Changed);
        Assert.Equal("hello Alice", await File.ReadAllTextAsync(destinationPath));

        var secondCheck = await checkModule.ExecuteAsync(CancellationToken.None);
        Assert.False(secondCheck.Changed);
    }
}
