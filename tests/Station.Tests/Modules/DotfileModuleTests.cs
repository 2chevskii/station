using System.Text.Json.Nodes;
using Station.Abstractions;
using Station.Core;
using Station.Modules.Dotfile;

namespace Station.Tests;

public sealed class DotfileModuleTests
{
    [Fact]
    public async Task Execute_is_idempotent_for_template_dotfile()
    {
        using var directory = new TemporaryDirectory();
        var taskRoot = Path.Combine(directory.Path, "role");
        Directory.CreateDirectory(Path.Combine(taskRoot, "templates"));
        var templatePath = Path.Combine(taskRoot, "templates", "app.txt");
        await File.WriteAllTextAsync(templatePath, "hello {{ name }}");

        var destinationPath = Path.Combine(directory.Path, "out.txt");
        var module = new DotfileModule(TimeProvider.System);

        var request = new ModuleExecutionRequest(
            ExecutionMode.Check,
            "render app",
            directory.Path,
            taskRoot,
            taskRoot,
            "base",
            taskRoot,
            new JsonObject
            {
                ["source"] = "templates/app.txt",
                ["destination"] = destinationPath,
            },
            new Dictionary<string, string> { ["name"] = "Alice" },
            false);

        var initialCheck = await module.ExecuteAsync(request, CancellationToken.None);
        Assert.True(initialCheck.Changed);

        var applyRequest = request with { Mode = ExecutionMode.Apply };
        var applyResult = await module.ExecuteAsync(applyRequest, CancellationToken.None);
        Assert.True(applyResult.Changed);
        Assert.Equal("hello Alice", await File.ReadAllTextAsync(destinationPath));

        var secondCheck = await module.ExecuteAsync(request, CancellationToken.None);
        Assert.False(secondCheck.Changed);
    }
}
