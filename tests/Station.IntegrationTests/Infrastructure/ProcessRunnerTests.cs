using System.Diagnostics;

using FluentAssertions;

using Station.Abstractions.FileSystem;
using Station.Abstractions.Processes;
using Station.Testing;
using Station.Utilities.Processes;

namespace Station.IntegrationTests.Infrastructure;

[Collection( "Environment variables" )]
public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Pre_cancelled_command_does_not_attempt_to_start_a_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new ProcessRunner();

        var action = () => runner.RunAsync(
            new ProcessCommand( "station-nonexistent-command", [] ),
            cancellation.Token
        );

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_terminates_the_running_process_and_its_child()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "child.ps1",
            """
            [System.IO.File]::WriteAllText("$env:STATION_CHILD_PID.tmp", [string]$PID)
            [System.IO.File]::Move("$env:STATION_CHILD_PID.tmp", $env:STATION_CHILD_PID)
            Start-Sleep -Seconds 60
            """
        );
        directory.WriteFile(
            "parent.ps1",
            """
            [System.IO.File]::WriteAllText("$env:STATION_PARENT_PID.tmp", [string]$PID)
            [System.IO.File]::Move("$env:STATION_PARENT_PID.tmp", $env:STATION_PARENT_PID)
            $childStart = [System.Diagnostics.ProcessStartInfo]::new($env:STATION_POWERSHELL_RUNNER)
            $childStart.UseShellExecute = $false
            $childStart.CreateNoWindow = $true
            $childStart.ArgumentList.Add('-NoLogo')
            $childStart.ArgumentList.Add('-NoProfile')
            $childStart.ArgumentList.Add('-File')
            $childStart.ArgumentList.Add($env:STATION_CHILD_SCRIPT)
            $child = [System.Diagnostics.Process]::Start($childStart)
            $child.WaitForExit()
            """
        );
        using var cancellation = new CancellationTokenSource();
        var runner = new ProcessRunner();
        var execution = runner.RunAsync(
            new ProcessCommand(
                "pwsh",
                ["-NoLogo", "-NoProfile", "-File", directory.GetPath( "parent.ps1" )],
                directory.Path,
                new Dictionary<string, string?>
                {
                    ["STATION_PARENT_PID"] = directory.GetPath( "parent.pid" ),
                    ["STATION_CHILD_PID"] = directory.GetPath( "child.pid" ),
                    ["STATION_CHILD_SCRIPT"] = directory.GetPath( "child.ps1" ),
                    ["STATION_POWERSHELL_RUNNER"] = CommandLocator.Find( "pwsh" ),
                }
            ),
            cancellation.Token
        );
        Process? parent = null;
        Process? child = null;

        try
        {
            parent = await ReadProcessAsync( directory.GetPath( "parent.pid" ) );
            child = await ReadProcessAsync( directory.GetPath( "child.pid" ) );
            parent.HasExited.Should().BeFalse();
            child.HasExited.Should().BeFalse();

            cancellation.Cancel();
            var action = () => execution.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            await action.Should().ThrowAsync<OperationCanceledException>();
            await parent.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 10 ) );
            parent.HasExited.Should().BeTrue();
            await child.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 10 ) );
            child.HasExited.Should().BeTrue();
        }
        finally
        {
            cancellation.Cancel();
            await StopProcessAsync( parent );
            await StopProcessAsync( child );
        }
    }

    [Fact]
    public async Task Command_environment_controls_PATH_resolution()
    {
        using var shims = new CommandShimScope();
        shims.AddCommand( "path-probe", "echo parent-path" );
        using var directory = new TemporaryDirectory();
        var commandName = OperatingSystem.IsWindows() ? "path-probe.cmd" : "path-probe";
        directory.WriteFile(
            commandName,
            OperatingSystem.IsWindows()
                ? "@echo off\r\necho command-path\r\n"
                : "#!/bin/sh\necho command-path\n"
        );
        if ( !OperatingSystem.IsWindows() )
            File.SetUnixFileMode(
                directory.GetPath( commandName ),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessCommand(
                "path-probe",
                [],
                Environment: new Dictionary<string, string?> { ["PATH"] = directory.Path }
            ),
            CancellationToken.None
        );

        result.ExitCode.Should().Be( 0, result.StandardError );
        result.StandardOutput.Trim().Should().Be( "command-path" );
    }

    [Fact]
    public async Task CommandExists_and_RunAsync_use_path_and_environment()
    {
        using var shims = new CommandShimScope();
        shims.AddPowerShellCommand(
            "echoargs",
            """
            Write-Output $env:TEST_VALUE
            Write-Output ($args -join ' ')
            """
        );
        if ( OperatingSystem.IsWindows() )
            File.WriteAllText( Path.Combine( shims.Path, "echoargs" ), "#!/bin/sh\necho wrong-shim\n" );

        var runner = new ProcessRunner();

        runner.CommandExists( "echoargs" ).Should().BeTrue();
        runner.CommandExists( shims.AddCommand( "rooted", "echo rooted" ) ).Should().BeTrue();
        runner.CommandExists( "missing-command" ).Should().BeFalse();

        var result = await runner.RunAsync(
            new ProcessCommand(
                "echoargs",
                ["hello", "world"],
                Environment.CurrentDirectory,
                new Dictionary<string, string?> { ["TEST_VALUE"] = "value" }
            ),
            CancellationToken.None
        );

        result.ExitCode.Should().Be( 0 );
        result.StandardOutput.Should().ContainAll( "value", "hello world" );
        new ProcessCommand( "tool", ["a", "b"] ).ToString().Should().Be( "tool a b" );
    }

    private static async Task<Process> ReadProcessAsync( string pidFile )
    {
        using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
        while ( true )
        {
            if ( File.Exists( pidFile ) )
            {
                var contents = await File.ReadAllTextAsync( pidFile, timeout.Token );
                if ( int.TryParse( contents, out var processId ) )
                    return Process.GetProcessById( processId );
            }

            await Task.Delay( 20, timeout.Token );
        }
    }

    private static async Task StopProcessAsync( Process? process )
    {
        if ( process is null )
            return;

        using ( process )
        {
            if ( !process.HasExited )
                process.Kill( entireProcessTree: true );

            await process.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 10 ) );
        }
    }
}
