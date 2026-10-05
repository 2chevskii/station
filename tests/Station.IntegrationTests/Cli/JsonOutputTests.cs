using System.Text.Json;

using FluentAssertions;

using Station.Testing;

namespace Station.IntegrationTests.Cli;

public sealed class JsonOutputTests
{
    [Fact]
    public async Task Task_statuses_and_structured_outputs_survive_partial_failure()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        directory.WriteFile( "ok.ps1", "Write-Output '{\"changed\":false}'" );
        directory.WriteFile( "changed.ps1", "Write-Output '{\"changed\":true}'" );
        directory.WriteFile( "skipped.ps1", "Write-Output '{\"skipped\":true}'" );
        const string message = "Partial \"failure\"\n[red]Данные 世界[/]";
        var payload = JsonSerializer.Serialize( new
        {
            changed = true,
            failed = true,
            message,
            outputs = new { nested = new { value = 42, items = new[] { "a", "b" } } },
        } );
        directory.WriteFile( "failed.ps1", $"Write-Output '{payload.Replace( "'", "''" )}'" );
        directory.WriteFile(
            "station.yaml",
            """
            roles: [empty]
            tasks:
              - script: { path: ok.ps1 }
              - script: { path: changed.ps1 }
              - script: { path: skipped.ps1 }
              - script: { path: failed.ps1 }
              - script: { path: unreachable.ps1 }
            """
        );

        var result = await CliRunner.RunAsync(
            "check", "--config", directory.Path, "--output", "json"
        );

        result.ExitCode.Should().Be( 1 );
        using var json = JsonDocument.Parse( result.StandardOutput );
        var report = json.RootElement;
        AssertEnvelope( report, "check", false, 1 );
        var data = report.GetProperty( "data" );
        var tasks = data.GetProperty( "tasks" ).EnumerateArray().ToArray();
        tasks.Select( task => task.GetProperty( "status" ).GetString() )
            .Should().Equal( "ok", "changed", "skipped", "failed" );
        tasks[3].GetProperty( "changed" ).GetBoolean().Should().BeTrue();
        tasks[3].GetProperty( "failed" ).GetBoolean().Should().BeTrue();
        tasks[3].GetProperty( "message" ).GetString().Should().Be( message );
        var nested = tasks[3].GetProperty( "outputs" ).GetProperty( "nested" );
        nested.GetProperty( "value" ).GetInt32().Should().Be( 42 );
        nested.GetProperty( "items" )[1].GetString().Should().Be( "b" );
        var summary = data.GetProperty( "summary" );
        summary.GetProperty( "taskCount" ).GetInt32().Should().Be( 4 );
        summary.GetProperty( "changedCount" ).GetInt32().Should().Be( 2 );
        summary.GetProperty( "failedCount" ).GetInt32().Should().Be( 1 );
        summary.GetProperty( "skippedCount" ).GetInt32().Should().Be( 1 );
    }

    [Fact]
    public async Task Failed_restore_reports_successfully_restored_plugins()
    {
        using var directory = new TemporaryDirectory();
        PluginFixtureBuilder.CreatePlugin( directory, "plugins/demo", "example.tools", "tooling" );
        directory.WriteFile(
            "station.yaml",
            "roles: [tooling]\nplugins: [demo, missing-plugin]\n"
        );

        var result = await CliRunner.RunAsync(
            "restore", "--config", directory.Path, "--output", "json"
        );

        result.ExitCode.Should().Be( 1 );
        using var json = JsonDocument.Parse( result.StandardOutput );
        var report = json.RootElement;
        AssertEnvelope( report, "restore", false, 1 );
        var plugins = report.GetProperty( "data" ).GetProperty( "plugins" );
        plugins.GetArrayLength().Should().Be( 1 );
        plugins[0].GetProperty( "pluginName" ).GetString().Should().Be( "example.tools" );
        plugins[0].GetProperty( "version" ).GetString().Should().Be( "1.0.0" );
        plugins[0].GetProperty( "isLocal" ).GetBoolean().Should().BeTrue();
        report.GetProperty( "diagnostics" ).ToString().Should().Contain( "missing-plugin" );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "  value=with spaces  " )]
    public async Task Variable_overrides_preserve_empty_values_and_whitespace( string value )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        directory.WriteFile( "templates/greeting.scriban", "{{ greeting }}" );
        directory.WriteFile(
            "station.yaml",
            $$"""
              roles: [empty]
              tasks:
                - template:
                    template: greeting
                    destination: {{directory.GetPath( "output.txt" )}}
              """
        );

        var result = await CliRunner.RunAsync(
            "apply", "--config", directory.Path, "--output", "json", "--set", $"greeting={value}"
        );

        result.ExitCode.Should().Be( 0, result.StandardError );
        using var json = JsonDocument.Parse( result.StandardOutput );
        AssertEnvelope( json.RootElement, "apply", true, 0 );
        File.ReadAllText( directory.GetPath( "output.txt" ) ).Should().Be( value );
    }

    [Theory]
    [InlineData( "validate" )]
    [InlineData( "check" )]
    [InlineData( "apply" )]
    public async Task Run_commands_emit_one_JSON_result_and_route_progress_to_stderr( string command )
    {
        using var directory = CreateConfiguration();

        var result = await CliRunner.RunAsync(
            command, "--config", directory.Path, "--output", "json"
        );

        result.ExitCode.Should().Be( 0, result.StandardError );
        using var json = JsonDocument.Parse( result.StandardOutput );
        var report = json.RootElement;
        AssertEnvelope( report, command, true, 0 );
        result.StandardError.Should().Contain( "Inspecting copy sample" );
        report.GetProperty( "diagnostics" ).GetArrayLength().Should().Be( 0 );
        var data = report.GetProperty( "data" );
        var tasks = data.GetProperty( "tasks" );
        tasks.GetArrayLength().Should().Be( command == "validate" ? 0 : 1 );
        if ( command != "validate" )
        {
            tasks[0].GetProperty( "taskName" ).GetString().Should().Be( "copy sample" );
            tasks[0].GetProperty( "moduleName" ).GetString().Should().Be( "station.builtin.copy" );
            tasks[0].GetProperty( "status" ).GetString().Should().Be( "changed" );
            tasks[0].GetProperty( "changed" ).GetBoolean().Should().BeTrue();
            data.GetProperty( "summary" ).GetProperty( "changedCount" ).GetInt32().Should().Be( 1 );
        }

        File.Exists( directory.GetPath( "output.txt" ) ).Should().Be( command == "apply" );
    }

    [Fact]
    public async Task Apply_JSON_preserves_completed_records_on_later_validation_failure()
    {
        using var directory = CreateConfiguration();
        File.AppendAllText(
            directory.GetPath( "station.yaml" ),
            "\n  - name: broken\n    missing.module: {}\n"
        );

        var result = await CliRunner.RunAsync(
            "apply", "--config", directory.Path, "--output", "json"
        );

        result.ExitCode.Should().Be( 1 );
        using var json = JsonDocument.Parse( result.StandardOutput );
        var report = json.RootElement;
        AssertEnvelope( report, "apply", false, 1 );
        report.GetProperty( "diagnostics" ).ToString().Should().Contain( "missing.module" );
        report.GetProperty( "data" ).GetProperty( "tasks" ).GetArrayLength().Should().Be( 1 );
        File.ReadAllText( directory.GetPath( "output.txt" ) ).Should().Be( "sample" );
    }

    [Theory]
    [InlineData( "restore" )]
    [InlineData( "explain" )]
    [InlineData( "list roles" )]
    [InlineData( "list modules" )]
    public async Task Discovery_commands_support_JSON_before_the_command( string command )
    {
        using var directory = CreateConfiguration();
        var arguments = new List<string> { "--output=json", "--config", directory.Path };
        arguments.AddRange( command.Split( ' ' ) );

        var result = await CliRunner.RunAsync( arguments.ToArray() );

        result.ExitCode.Should().Be( 0, result.StandardError );
        using var json = JsonDocument.Parse( result.StandardOutput );
        var report = json.RootElement;
        AssertEnvelope( report, command, true, 0 );
        var data = report.GetProperty( "data" );
        if ( command == "restore" )
            data.GetProperty( "plugins" ).GetArrayLength().Should().Be( 0 );
        else if ( command == "explain" )
            data.GetProperty( "plan" ).GetProperty( "kind" ).GetString().Should().Be( "root" );
        else
            data.GetProperty( "components" ).GetArrayLength().Should().BeGreaterThan( 0 );

        File.Exists( directory.GetPath( "output.txt" ) ).Should().BeFalse();
    }

    [Theory]
    [InlineData( "--unknown" )]
    [InlineData( "--set", "invalid" )]
    [InlineData( "--set" )]
    [InlineData( "--set", "key=value", "--set" )]
    [InlineData( "--set", "--set", "key=value" )]
    [InlineData( "--config" )]
    [InlineData( "--config", "." )]
    [InlineData( "--output", "json" )]
    [InlineData( "--output=json" )]
    [InlineData( "--plugin-cache-dir", "cache", "--plugin-cache-dir", "cache" )]
    public async Task Invalid_arguments_return_JSON_without_executing( params string[] invalidArguments )
    {
        using var directory = CreateConfiguration();
        var arguments = new List<string>
        {
            "apply", "--config", directory.Path, "--output", "json",
        };
        arguments.AddRange( invalidArguments );

        var result = await CliRunner.RunAsync( arguments.ToArray() );

        result.ExitCode.Should().Be( 1 );
        using var json = JsonDocument.Parse( result.StandardOutput );
        AssertEnvelope( json.RootElement, "apply", false, 1 );
        json.RootElement.GetProperty( "diagnostics" ).GetArrayLength().Should().BeGreaterThan( 0 );
        json.RootElement.GetProperty( "data" ).ValueKind.Should().Be( JsonValueKind.Null );
        File.Exists( directory.GetPath( "output.txt" ) ).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_configuration_is_a_failed_JSON_result()
    {
        using var directory = new TemporaryDirectory();

        var result = await CliRunner.RunAsync(
            "check", "--config", directory.Path, "--output", "json"
        );

        result.ExitCode.Should().Be( 1 );
        using var json = JsonDocument.Parse( result.StandardOutput );
        AssertEnvelope( json.RootElement, "check", false, 1 );
        json.RootElement.GetProperty( "diagnostics" ).GetArrayLength().Should().BeGreaterThan( 0 );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "list" )]
    public async Task Missing_subcommand_returns_a_JSON_error( string command )
    {
        var arguments = new List<string> { "--output", "json" };
        if ( command.Length > 0 )
            arguments.Add( command );

        var result = await CliRunner.RunAsync( arguments.ToArray() );

        result.ExitCode.Should().Be( 1 );
        using var json = JsonDocument.Parse( result.StandardOutput );
        AssertEnvelope( json.RootElement, command.Length == 0 ? "station" : "list", false, 1 );
    }

    [Fact]
    public async Task Help_keeps_the_standard_help_output_when_JSON_is_selected()
    {
        var result = await CliRunner.RunAsync( "check", "--output", "json", "--help" );

        result.ExitCode.Should().Be( 0 );
        result.StandardOutput.Should().ContainAll( "--output", "--config", "--set" );
        result.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_output_format_is_rejected_before_executing_tasks()
    {
        using var directory = CreateConfiguration();

        var result = await CliRunner.RunAsync(
            "apply", "--config", directory.Path, "--output", "yaml"
        );

        result.ExitCode.Should().Be( 1 );
        result.StandardError.Should().Contain( "yaml" );
        File.Exists( directory.GetPath( "output.txt" ) ).Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_variable_overrides_work_before_the_command()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        directory.WriteFile( "templates/greeting.scriban", "{{ greeting }} {{ punctuation }}" );
        directory.WriteFile(
            "station.yaml",
            $$"""
              roles: [empty]
              tasks:
                - template:
                    template: greeting
                    destination: {{directory.GetPath( "output.txt" )}}
              """
        );

        var result = await CliRunner.RunAsync(
            "--output", "JSON", "--config", directory.Path,
            "--set", "greeting=old", "punctuation=!", "--set", "greeting=hello", "apply"
        );

        result.ExitCode.Should().Be( 0, result.StandardError );
        using var json = JsonDocument.Parse( result.StandardOutput );
        AssertEnvelope( json.RootElement, "apply", true, 0 );
        File.ReadAllText( directory.GetPath( "output.txt" ) ).Should().Be( "hello !" );
    }

    private static TemporaryDirectory CreateConfiguration()
    {
        var directory = new TemporaryDirectory();
        directory.WriteFile( "files/input.txt", "sample" );
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        directory.WriteFile(
            "station.yaml",
            $$"""
              roles: [empty]
              tasks:
                - name: copy sample
                  copy:
                    file: input.txt
                    destination: {{directory.GetPath( "output.txt" )}}
              """
        );
        return directory;
    }

    private static void AssertEnvelope( JsonElement report, string command, bool succeeded, int exitCode )
    {
        report.GetProperty( "schemaVersion" ).GetInt32().Should().Be( 1 );
        report.GetProperty( "command" ).GetString().Should().Be( command );
        report.GetProperty( "succeeded" ).GetBoolean().Should().Be( succeeded );
        report.GetProperty( "exitCode" ).GetInt32().Should().Be( exitCode );
        report.GetProperty( "message" ).GetString().Should().NotBeNullOrWhiteSpace();
    }
}
