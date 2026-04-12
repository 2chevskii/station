using Spectre.Console.Testing;
using Station.Abstractions.Models;
using Station.Core.Execution.Models;
using Station.Core.Plugins.Models;

namespace Station.Tests.Cli;

public sealed class StationConsoleTests
{
    [Fact]
    public void WriteResult_RendersStatusAndTasks()
    {
        var standard = new TestConsole().Width(160);
        var error = new TestConsole().Width(160);
        var console = new StationConsole(standard, error);
        var result = new EngineRunResult(
            Station.Abstractions.Enums.ExecutionMode.Check,
            true,
            "Check completed. 1 task(s) would change.",
            [],
            [
                new EngineTaskRecord(
                    "Install scoop package",
                    "builtin.scoop",
                    null,
                    ModuleResult.ChangedResult("Package would be installed"))
            ]);

        console.WriteResult(result);

        var output = standard.Output;
        Assert.Contains("Success", output);
        Assert.Contains("Install scoop package", output);
        Assert.Contains("builtin.scoop", output);
        Assert.Contains("changed", output);
        Assert.Contains("Package would be installed", output);
        Assert.Equal(string.Empty, error.Output);
    }

    [Fact]
    public void WriteRestoreResult_RendersPluginSource()
    {
        var standard = new TestConsole().Width(160);
        var error = new TestConsole().Width(160);
        var console = new StationConsole(standard, error);
        var result = new PluginRestoreResult(
            true,
            "Resolved plugins.",
            [],
            [
                new PluginRestoreRecord(
                    "station/example",
                    "example.tools",
                    "1.2.3",
                    "C:\\cache\\example",
                    CacheHit: true,
                    IsLocal: false)
            ],
            []);

        console.WriteRestoreResult(result);

        var output = standard.Output;
        Assert.Contains("Resolved plugins.", output);
        Assert.Contains("example.tools", output);
        Assert.Contains("1.2.3", output);
        Assert.Contains("cache", output);
        Assert.Contains("C:\\cache\\example", output);
        Assert.Equal(string.Empty, error.Output);
    }

    [Fact]
    public void WriteExplainResult_RendersPlanAsTree()
    {
        var standard = new TestConsole().Width(160);
        var error = new TestConsole().Width(160);
        var console = new StationConsole(standard, error);
        var result = new ExplainResult(
            true,
            "Explained 1 task(s).",
            [],
            new ExplainNode(
                "station",
                ExplainNodeKind.Root,
                [
                    new ExplainNode(
                        "tasks",
                        ExplainNodeKind.Section,
                        [
                            new ExplainNode(
                                "base.tasks.1 [station.builtin.template]",
                                ExplainNodeKind.Task,
                                [
                                    new ExplainNode("role: base", ExplainNodeKind.Detail, []),
                                    new ExplainNode("parameters", ExplainNodeKind.Section, [
                                        new ExplainNode("template = sample", ExplainNodeKind.Value, [])
                                    ])
                                ])
                        ])
                ]));

        console.WriteExplainResult(result);

        var output = standard.Output;
        Assert.Contains("station", output);
        Assert.Contains("tasks", output);
        Assert.Contains("base.tasks.1 [station.builtin.template]", output);
        Assert.Contains("template = sample", output);
        Assert.Equal(string.Empty, error.Output);
    }
}
