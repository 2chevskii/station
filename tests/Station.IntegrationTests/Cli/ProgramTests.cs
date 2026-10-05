using FluentAssertions;

using Station.Abstractions.FileSystem;
using Station.Testing;

using TruePath.SystemIo;

namespace Station.IntegrationTests.Cli;

public sealed class ProgramTests
{
    [Fact]
    public async Task Apply_command_returns_failure_and_stops_at_an_unknown_module()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "files/input.txt", "copied" );
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        directory.WriteFile(
            "station.yaml",
            $$"""
              roles: [empty]
              tasks:
                - name: completed
                  copy:
                    file: input.txt
                    destination: {{directory.GetPath( "completed.txt" )}}
                - name: broken
                  missing.module: {}
                - name: unreachable
                  copy:
                    file: input.txt
                    destination: {{directory.GetPath( "unreachable.txt" )}}
              """
        );

        var result = await CliRunner.RunAsync( "apply", "--config", directory.Path );

        result.ExitCode.Should().Be( 1 );
        result.StandardError.Should().ContainAll( "broken", "missing.module", "completed" );
        File.ReadAllText( directory.GetPath( "completed.txt" ) ).Should().Be( "copied" );
        File.Exists( directory.GetPath( "unreachable.txt" ) ).Should().BeFalse();
    }

    [Fact]
    public async Task Help_command_returns_zero_and_lists_commands()
    {
        var result = await CliRunner.RunAsync( "--help" );

        result.ExitCode.Should().Be( 0 );
        result
            .StandardOutput.Should()
            .ContainAll( "restore", "explain", "list", "validate", "check", "apply" );
    }

    [Fact]
    public async Task List_help_shows_nested_commands_and_shared_options()
    {
        var result = await CliRunner.RunAsync( "list", "--help" );

        result.ExitCode.Should().Be( 0 );
        result.StandardOutput.Should().ContainAll(
            "roles", "modules", "--config", "--set", "--plugin-cache-dir", "--output"
        );
        result.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task Explain_command_renders_plan_for_example_config()
    {
        var configRoot = (CliRunner.FindRepositoryRoot().AsAbsolutePath() / "examples" / "workstation").Value;

        var result = await CliRunner.RunAsync( "explain", "--config", configRoot );

        result.ExitCode.Should().Be( 0 );
        result.StandardOutput.Should().Contain( "station" );
        result.StandardOutput.Should().Contain( "tasks" );
    }

    [Fact]
    public async Task List_roles_command_shows_descriptions_and_sources()
    {
        var configRoot = (CliRunner.FindRepositoryRoot().AsAbsolutePath() / "examples" / "workstation").Value;

        var result = await CliRunner.RunAsync( "list", "roles", "--config", configRoot );

        result.ExitCode.Should().Be( 0 );
        result
            .StandardOutput.Should()
            .ContainAll( "station.local.git", "Configures Git", "preferences and user", "Source" );
    }

    [Fact]
    public async Task List_modules_command_shows_descriptions_and_sources()
    {
        var configRoot = (CliRunner.FindRepositoryRoot().AsAbsolutePath() / "examples" / "workstation").Value;

        var result = await CliRunner.RunAsync( "list", "modules", "--config", configRoot );

        result.ExitCode.Should().Be( 0 );
        result
            .StandardOutput.Should()
            .ContainAll( "station.builtin.copy", "Copies a file", "station.builtin" );
    }

    [Fact]
    public async Task Restore_command_accepts_plugin_cache_directory_option()
    {
        using var directory = new TemporaryDirectory();
        using var source = new TemporaryDirectory();
        var pluginRoot = PluginFixtureBuilder.CreatePlugin(
            source,
            "archive-plugin",
            "archive.plugin",
            "plugin-role"
        );
        var archivePath = directory.GetPath( "plugins", "demo_plugin.zip" );
        archivePath.AsAbsolutePath().Parent!.Value.CreateDirectory();
        ArchiveBuilder.CreateZip( pluginRoot, archivePath );
        directory.WriteFile( "station.yaml", "roles: [plugin-role]\nplugins: [demo_plugin.zip]\n" );
        var cacheDirectory = directory.GetPath( "external-cache" );

        var result = await CliRunner.RunAsync(
            "restore",
            "--config",
            directory.Path,
            "--plugin-cache-dir",
            cacheDirectory
        );

        result.ExitCode.Should().Be( 0 );
        directory
            .GetPath( "external-cache", "archive.plugin", "1.0.0", "manifest.yaml" )
            .AsAbsolutePath()
            .ExistsFile()
            .Should()
            .BeTrue();
    }

}
