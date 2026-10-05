using FluentAssertions;

using Spectre.Console.Testing;

using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Core.Plugins.Restoration;

namespace Station.UnitTests.Core;

public sealed class StationConsoleTests
{
    [Fact]
    public void WriteResult_renders_status_tasks_stats_and_diagnostics()
    {
        var standard = new TestConsole().Width( 160 );
        var error = new TestConsole().Width( 160 );
        var console = new StationConsole( standard, error );
        var result = new EngineRunResult(
            ExecutionMode.Check,
            true,
            "Check completed. 1 task(s) would change.",
            ["warn"],
            [
                new EngineTaskRecord(
                    "Install scoop package",
                    "builtin.scoop",
                    null,
                    ModuleResult.ChangedResult( "Package would be installed" )
                ),
            ]
        );

        console.WriteResult( result );

        standard
            .Output.Should()
            .ContainAll(
                "Success",
                "Install scoop package",
                "changed",
                "Diagnostics",
                "warn",
                "Summary"
            );
        error.Output.Should().BeEmpty();
    }

    [Fact]
    public void WriteRestoreResult_routes_failures_to_error_console()
    {
        var standard = new TestConsole().Width( 160 );
        var error = new TestConsole().Width( 160 );
        var console = new StationConsole( standard, error );
        var result = new PluginRestoreResult(
            false,
            "Plugin restore failed.",
            ["failure"],
            [
                new PluginRestoreRecord(
                    "station/example",
                    "example.tools",
                    "1.2.3",
                    "C:\\cache\\example",
                    true,
                    false
                ),
            ],
            []
        );

        console.WriteRestoreResult( result );

        error.Output.Should().ContainAll( "Failed", "example.tools", "cache", "failure" );
        standard.Output.Should().BeEmpty();
    }

    [Fact]
    public void WriteExplainResult_renders_tree()
    {
        var standard = new TestConsole().Width( 160 );
        var error = new TestConsole().Width( 160 );
        var console = new StationConsole( standard, error );
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
                                    new ExplainNode( "role: base", ExplainNodeKind.Detail, [] ),
                                    new ExplainNode(
                                        "parameters",
                                        ExplainNodeKind.Section,
                                        [
                                            new ExplainNode(
                                                "template = sample",
                                                ExplainNodeKind.Value,
                                                []
                                            ),
                                        ]
                                    ),
                                ]
                            ),
                        ]
                    ),
                ]
            )
        );

        console.WriteExplainResult( result );

        standard
            .Output.Should()
            .ContainAll(
                "station",
                "tasks",
                "base.tasks.1 [station.builtin.template]",
                "template = sample"
            );
        error.Output.Should().BeEmpty();
    }
}