using System.Diagnostics;

using FluentAssertions;

using Station.Abstractions.Configuration;
using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Modules.Builtin.Script;
using Station.Testing;
using Station.Utilities.Processes;

namespace Station.IntegrationTests.Modules;

public sealed class ScriptModuleTests
{
    [Fact]
    public async Task Cancellation_stops_script_and_deletes_its_request_file()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "script.ps1",
            """
            param([string]$requestPath)
            [System.IO.File]::WriteAllText("$PSScriptRoot/request-path.txt", $requestPath)
            [System.IO.File]::WriteAllText("$PSScriptRoot/ready.tmp", [string]$PID)
            [System.IO.File]::Move("$PSScriptRoot/ready.tmp", "$PSScriptRoot/ready.pid")
            Start-Sleep -Seconds 60
            """
        );
        var module = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.Path ),
            new ProcessRunner()
        )
        {
            ScriptPath = "script.ps1",
        };
        using var cancellation = new CancellationTokenSource();
        var execution = module.ExecuteAsync( cancellation.Token ).AsTask();
        Process? script = null;

        try
        {
            using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
            while ( !File.Exists( directory.GetPath( "ready.pid" ) ) )
                await Task.Delay( 20, timeout.Token );

            var processId = int.Parse(
                await File.ReadAllTextAsync( directory.GetPath( "ready.pid" ), timeout.Token )
            );
            script = Process.GetProcessById( processId );
            var requestPath = await File.ReadAllTextAsync(
                directory.GetPath( "request-path.txt" ),
                timeout.Token
            );
            File.Exists( requestPath ).Should().BeTrue();

            cancellation.Cancel();
            var action = () => execution.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            await action.Should().ThrowAsync<OperationCanceledException>();
            await script.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 10 ) );
            script.HasExited.Should().BeTrue();
            File.Exists( requestPath ).Should().BeFalse();
        }
        finally
        {
            cancellation.Cancel();
            if ( script is not null )
            {
                using ( script )
                {
                    if ( !script.HasExited )
                        script.Kill( entireProcessTree: true );

                    await script.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 10 ) );
                }
            }
        }
    }

    [Fact]
    public async Task Execute_runs_script_and_parses_json_result()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "role/scripts/sample.ps1",
            """
            param([string]$requestPath)
            $request = Get-Content $requestPath | ConvertFrom-Json
            @{ changed = $true; message = "Hello $($request.variables.name)" } | ConvertTo-Json -Compress
            """
        );
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String( "Alice" ) }
            )
        );
        var module = new ScriptModule(
            TestModuleContextFactory.Create(
                directory.Path,
                directory.GetPath( "role" ),
                variables,
                mode: ExecutionMode.Apply
            ),
            new ProcessRunner()
        )
        {
            ScriptPath = "scripts/sample.ps1",
        };

        (await module.InspectAsync( CancellationToken.None )).IsValid.Should().BeTrue();
        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Changed.Should().BeTrue();
        result.Message.Should().Be( "Hello Alice" );
    }

    [Fact]
    public async Task Execute_returns_failures_for_empty_invalid_and_nonzero_output()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "role/scripts/empty.ps1", "" );
        directory.WriteFile( "role/scripts/invalid.ps1", "Write-Output 'not json'" );
        directory.WriteFile( "role/scripts/wrong-shape.ps1", "Write-Output '{\"changed\":\"true\"}'" );
        directory.WriteFile( "role/scripts/fail.ps1", "Write-Error 'boom'; exit 1" );

        (await ExecuteAsync( directory, "empty" )).Failed.Should().BeTrue();
        (await ExecuteAsync( directory, "invalid" )).Message.Should().Contain( "invalid JSON" );
        (await ExecuteAsync( directory, "wrong-shape" )).Message.Should().Contain( "must be a boolean" );
        (await ExecuteAsync( directory, "fail" )).Message.Should().Contain( "boom" );
        return;

        static async Task<ModuleResult> ExecuteAsync(
            TemporaryDirectory directory,
            string scriptName
        )
        {
            var module = new ScriptModule(
                TestModuleContextFactory.Create( directory.Path, directory.GetPath( "role" ) ),
                new ProcessRunner()
            )
            {
                ScriptPath = $"scripts/{scriptName}.ps1",
            };
            return await module.ExecuteAsync( CancellationToken.None );
        }
    }

    [Fact]
    public async Task Inspect_rejects_missing_runner_missing_script_and_path_escape()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "role/scripts/sample.ps1", "Write-Output '{\"changed\":false}'" );

        var missingScript = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.GetPath( "role" ) ),
            new ProcessRunner()
        )
        {
            ScriptPath = "scripts/missing.ps1",
        };
        var missingRunner = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.GetPath( "role" ) ),
            new ProcessRunner()
        )
        {
            ScriptPath = "scripts/sample.ps1",
            Runner = "definitely-missing-runner",
        };
        var escapedPath = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.GetPath( "role" ) ),
            new ProcessRunner()
        )
        {
            ScriptPath = Path.Combine( "..", "outside.ps1" ),
        };
        var siblingPath = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.GetPath( "role" ) ),
            new ProcessRunner()
        )
        {
            ScriptPath = Path.Combine( "..", "role-other", "sample.ps1" ),
        };

        (await missingScript.InspectAsync( CancellationToken.None )).IsValid.Should().BeFalse();
        (await missingRunner.InspectAsync( CancellationToken.None ))
            .Diagnostics.Should()
            .ContainSingle( x =>
                x.Message.Contains( "not available", StringComparison.OrdinalIgnoreCase )
            );
        (await escapedPath.ExecuteAsync( CancellationToken.None ))
            .Message.Should()
            .Contain( "must stay within the task root" );
        (await siblingPath.ExecuteAsync( CancellationToken.None ))
            .Message.Should()
            .Contain( "must stay within the task root" );
    }
}
