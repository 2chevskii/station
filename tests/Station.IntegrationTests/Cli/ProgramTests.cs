using System.Diagnostics;
using FluentAssertions;
using Station.Abstractions.FileSystem;
using Station.Testing;
using TruePath;
using TruePath.SystemIo;

namespace Station.IntegrationTests.Cli;

public sealed class ProgramTests
{
    [Fact]
    public async Task Help_command_returns_zero_and_lists_commands()
    {
        var result = await RunCliAsync("--help");

        result.ExitCode.Should().Be(0);
        result
            .StandardOutput.Should()
            .ContainAll("restore", "explain", "validate", "check", "apply");
    }

    [Fact]
    public async Task Explain_command_renders_plan_for_example_config()
    {
        var configRoot = (FindRepositoryRoot().AsAbsolutePath() / "examples" / "workstation").Value;

        var result = await RunCliAsync("explain", "--config", configRoot);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("station");
        result.StandardOutput.Should().Contain("tasks");
    }

    private static async Task<(
        int ExitCode,
        string StandardOutput,
        string StandardError
    )> RunCliAsync(params string[] arguments)
    {
        var stationCliAssembly = typeof(StationConsole).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot(),
        };
        startInfo.ArgumentList.Add(stationCliAssembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FindRepositoryRoot()
    {
        AbsolutePath? directory = AppContext.BaseDirectory.AsAbsolutePath();
        while (directory is { } current && !(current / "Station.slnx").ExistsFile())
        {
            directory = current.Parent;
        }

        return directory?.Value
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
