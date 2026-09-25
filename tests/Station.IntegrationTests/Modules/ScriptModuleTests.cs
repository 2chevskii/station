using FluentAssertions;
using Station.Abstractions.Enums;
using Station.Abstractions.Models;
using Station.Modules.Builtin.Script;
using Station.Testing;

namespace Station.IntegrationTests.Modules;

public sealed class ScriptModuleTests
{
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
                new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String("Alice") }
            )
        );
        var module = new ScriptModule(
            TestModuleContextFactory.Create(
                directory.Path,
                directory.GetPath("role"),
                variables,
                mode: ExecutionMode.Apply
            )
        )
        {
            ScriptPath = "scripts/sample.ps1",
        };

        (await module.InspectAsync(CancellationToken.None)).IsValid.Should().BeTrue();
        var result = await module.ExecuteAsync(CancellationToken.None);

        result.Changed.Should().BeTrue();
        result.Message.Should().Be("Hello Alice");
    }

    [Fact]
    public async Task Execute_returns_failures_for_empty_invalid_and_nonzero_output()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("role/scripts/empty.ps1", "");
        directory.WriteFile("role/scripts/invalid.ps1", "Write-Output 'not json'");
        directory.WriteFile("role/scripts/fail.ps1", "Write-Error 'boom'; exit 1");

        async Task<ModuleResult> ExecuteAsync(string scriptName)
        {
            var module = new ScriptModule(
                TestModuleContextFactory.Create(directory.Path, directory.GetPath("role"))
            )
            {
                ScriptPath = $"scripts/{scriptName}.ps1",
            };
            return await module.ExecuteAsync(CancellationToken.None);
        }

        (await ExecuteAsync("empty")).Failed.Should().BeTrue();
        (await ExecuteAsync("invalid")).Message.Should().Contain("invalid JSON");
        (await ExecuteAsync("fail")).Message.Should().Contain("boom");
    }

    [Fact]
    public async Task Inspect_rejects_missing_runner_missing_script_and_path_escape()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("role/scripts/sample.ps1", "Write-Output '{\"changed\":false}'");

        var missingScript = new ScriptModule(
            TestModuleContextFactory.Create(directory.Path, directory.GetPath("role"))
        )
        {
            ScriptPath = "scripts/missing.ps1",
        };
        var missingRunner = new ScriptModule(
            TestModuleContextFactory.Create(directory.Path, directory.GetPath("role"))
        )
        {
            ScriptPath = "scripts/sample.ps1",
            Runner = "definitely-missing-runner",
        };
        var escapedPath = new ScriptModule(
            TestModuleContextFactory.Create(directory.Path, directory.GetPath("role"))
        )
        {
            ScriptPath = "..\\outside.ps1",
        };

        (await missingScript.InspectAsync(CancellationToken.None)).IsValid.Should().BeFalse();
        (await missingRunner.InspectAsync(CancellationToken.None))
            .Diagnostics.Should()
            .ContainSingle(x =>
                x.Message.Contains("not available", StringComparison.OrdinalIgnoreCase)
            );
        (await escapedPath.ExecuteAsync(CancellationToken.None))
            .Message.Should()
            .Contain("must stay within the task root");
    }
}
