using FluentAssertions;
using Station.Abstractions.Execution;
using Station.Abstractions.FileSystem;
using Station.Core.Execution.Running;
using Station.Testing;

using TruePath.SystemIo;

namespace Station.IntegrationTests.Execution;

[Collection("Environment variables")]
public sealed class PluginCacheDirectoryTests
{
    [Fact]
    public async Task Restore_uses_directory_from_station_variables()
    {
        using var directory = CreateConfiguration(
            """
            vars:
              plugin_cache_dir: custom/plugins
            """
        );

        var result = await new ConfigEngine().RestoreAsync(directory.Path);

        result.Succeeded.Should().BeTrue();
        result.Plugins.Should().ContainSingle().Which.PluginRoot.Should().Be(
            directory.GetPath("custom", "plugins", "archive.plugin", "1.0.0")
        );
    }

    [Fact]
    public async Task Restore_environment_variable_overrides_station_variables()
    {
        using var directory = CreateConfiguration(
            """
            vars:
              plugin_cache_dir: config-cache
            """
        );
        using var environment = new EnvironmentVariableScope(
            "CONFIG_TOOL_VAR_PLUGIN_CACHE_DIR",
            "environment-cache"
        );

        var result = await new ConfigEngine().RestoreAsync(directory.Path);

        result.Succeeded.Should().BeTrue();
        result.Plugins.Should().ContainSingle().Which.PluginRoot.Should().Be(
            directory.GetPath("environment-cache", "archive.plugin", "1.0.0")
        );
    }

    [Fact]
    public async Task Restore_flag_overrides_set_and_environment_variable()
    {
        using var directory = CreateConfiguration(
            """
            vars:
              plugin_cache_dir: config-cache
            """
        );
        using var environment = new EnvironmentVariableScope(
            "CONFIG_TOOL_VAR_PLUGIN_CACHE_DIR",
            "environment-cache"
        );

        var engine = new ConfigEngine();
        var variables = new Dictionary<string, string> { ["plugin_cache_dir"] = "set-cache" };
        var fromSet = await engine.RestoreAsync(directory.Path, variables);
        var fromFlag = await engine.RestoreAsync(
            directory.Path,
            variables,
            pluginCacheDirectory: "flag-cache"
        );

        fromSet.Succeeded.Should().BeTrue();
        fromSet.Plugins.Should().ContainSingle().Which.PluginRoot.Should().Be(
            directory.GetPath("set-cache", "archive.plugin", "1.0.0")
        );
        fromFlag.Succeeded.Should().BeTrue();
        fromFlag.Plugins.Should().ContainSingle().Which.PluginRoot.Should().Be(
            directory.GetPath("flag-cache", "archive.plugin", "1.0.0")
        );
    }

    [Fact]
    public async Task List_and_validate_use_configured_cache_directory()
    {
        using var directory = CreateConfiguration(
            """
            vars:
              plugin_cache_dir: shared-cache
            """
        );
        var engine = new ConfigEngine();

        var listed = await engine.ListRolesAsync(directory.Path);
        var validated = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        listed.Succeeded.Should().BeTrue();
        listed.Components.Should().Contain(component =>
            component.Name == "archive.plugin.plugin-role"
            && component.Source.Contains("shared-cache/archive.plugin/1.0.0")
        );
        validated.Succeeded.Should().BeTrue();
        directory.GetPath("shared-cache", "archive.plugin", "1.0.0", "manifest.yaml")
            .AsAbsolutePath().ExistsFile().Should().BeTrue();
    }

    private static TemporaryDirectory CreateConfiguration(string extraManifest)
    {
        var directory = new TemporaryDirectory();
        using var source = new TemporaryDirectory();
        var pluginRoot = PluginFixtureBuilder.CreatePlugin(
            source,
            "archive-plugin",
            "archive.plugin",
            "plugin-role"
        );
        var archivePath = directory.GetPath("plugins", "demo_plugin.zip");
        archivePath.AsAbsolutePath().Parent!.Value.CreateDirectory();
        ArchiveBuilder.CreateZip(pluginRoot, archivePath);
        directory.WriteFile(
            "station.yaml",
            $"roles: [plugin-role]\nplugins: [demo_plugin.zip]\n{extraManifest}"
        );
        return directory;
    }
}
