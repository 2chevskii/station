using FluentAssertions;
using Station.Testing;
using Station.Utilities.Processes;

namespace Station.IntegrationTests.Infrastructure;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task CommandExists_and_RunAsync_use_path_and_environment()
    {
        using var shims = new CommandShimScope();
        var commandPath = shims.AddPowerShellCommand(
            "echoargs",
            """
            Write-Output $env:TEST_VALUE
            Write-Output ($args -join ' ')
            """
        );

        var runner = new ProcessRunner();

        runner.CommandExists("echoargs").Should().BeTrue();
        runner.CommandExists(shims.AddCommand("rooted", "echo rooted")).Should().BeTrue();
        runner.CommandExists("missing-command").Should().BeFalse();

        var result = await runner.RunAsync(
            new ProcessCommand(
                commandPath,
                ["hello", "world"],
                Environment.CurrentDirectory,
                new Dictionary<string, string?> { ["TEST_VALUE"] = "value" }
            ),
            CancellationToken.None
        );

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().ContainAll("value", "hello world");
        new ProcessCommand("tool", ["a", "b"]).ToString().Should().Be("tool a b");
    }
}
