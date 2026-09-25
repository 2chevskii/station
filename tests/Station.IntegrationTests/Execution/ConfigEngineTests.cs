using System.IO.Compression;
using FluentAssertions;
using Station.Abstractions.Enums;
using Station.Core.Execution.Services;
using Station.Testing;
using TruePath;
using TruePath.SystemIo;

namespace Station.IntegrationTests.Execution;

public sealed class ConfigEngineTests
{
    [Fact]
    public async Task Restore_returns_plugin_records_for_local_plugins()
    {
        using var directory = new TemporaryDirectory();
        PluginFixtureBuilder.CreatePlugin(
            directory,
            "plugins/demo_plugin",
            "example.plugin",
            "plugin-role"
        );
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - plugin-role
            plugins:
              - demo_plugin
            vars: {}
            """
        );

        var engine = new ConfigEngine();
        var result = await engine.RestoreAsync(directory.Path);

        result.Succeeded.Should().BeTrue();
        var plugin = result.Plugins.Should().ContainSingle().Subject;
        plugin.PluginName.Should().Be("example.plugin");
        plugin.IsLocal.Should().BeTrue();
        plugin.CacheHit.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_restores_local_archive_plugins_into_cache()
    {
        using var directory = new TemporaryDirectory();
        using var source = new TemporaryDirectory();
        var pluginRoot = PluginFixtureBuilder.CreatePlugin(
            source,
            "archive-plugin",
            "archive.plugin",
            "plugin-role"
        );
        var archivePath = directory.GetPath("plugins", "demo_plugin.zip");
        new AbsolutePath(archivePath).Parent!.Value.CreateDirectory();
        ZipFile.CreateFromDirectory(pluginRoot, archivePath);
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - plugin-role
            plugins:
              - demo_plugin.zip
            vars: {}
            """
        );

        var engine = new ConfigEngine();
        var first = await engine.RestoreAsync(directory.Path);
        var second = await engine.RestoreAsync(directory.Path);

        first.Succeeded.Should().BeTrue();
        first.Plugins.Should().ContainSingle().Subject.CacheHit.Should().BeFalse();
        second.Plugins.Should().ContainSingle().Subject.CacheHit.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_supports_git_url_nuget_and_npm_providers()
    {
        using var directory = new TemporaryDirectory();
        using var packages = new TemporaryDirectory();
        await using var server = new LocalHttpServer();

        var gitPluginRoot = PluginFixtureBuilder.CreatePlugin(
            packages,
            "git-plugin",
            "git.plugin",
            "git-role"
        );
        await InitializeGitRepositoryAsync(gitPluginRoot);

        var urlPluginRoot = PluginFixtureBuilder.CreatePlugin(
            packages,
            "url-plugin",
            "url.plugin",
            "url-role"
        );
        var urlArchivePath = packages.GetPath("url-plugin.zip");
        ArchiveBuilder.CreateZip(urlPluginRoot, urlArchivePath);

        var nugetPluginRoot = PluginFixtureBuilder.CreatePlugin(
            packages,
            "nuget-plugin",
            "nuget.plugin",
            "nuget-role"
        );
        var nugetPackagePath = packages.GetPath("station.demo.plugin.1.2.3.nupkg");
        ArchiveBuilder.CreateZip(nugetPluginRoot, nugetPackagePath);

        var npmPluginRoot = PluginFixtureBuilder.CreatePlugin(
            packages,
            "npm-plugin",
            "npm.plugin",
            "npm-role"
        );
        var npmPackagePath = packages.GetPath("station-demo-plugin-2.0.0.zip");
        ArchiveBuilder.CreateZip(npmPluginRoot, npmPackagePath);

        server.AddFile("/packages/url-plugin.zip", await new AbsolutePath(urlArchivePath).ReadAllBytesAsync());
        server.AddJson(
            "/nuget/index.json",
            $$"""
            {
              "resources": [
                {
                  "@id": "{{server.BaseUrl}}/flatcontainer/",
                  "@type": "PackageBaseAddress/3.0.0"
                }
              ]
            }
            """
        );
        server.AddJson(
            "/flatcontainer/station.demo.plugin/index.json",
            """
            {
              "versions": [ "1.2.3" ]
            }
            """
        );
        server.AddFile(
            "/flatcontainer/station.demo.plugin/1.2.3/station.demo.plugin.1.2.3.nupkg",
            await new AbsolutePath(nugetPackagePath).ReadAllBytesAsync()
        );
        server.AddJson(
            "/npm/station-demo-plugin",
            $$"""
            {
              "dist-tags": {
                "latest": "2.0.0"
              },
              "versions": {
                "2.0.0": {
                  "dist": {
                    "tarball": "{{server.BaseUrl}}/npm/station-demo-plugin/-/station-demo-plugin-2.0.0.zip"
                  }
                }
              }
            }
            """
        );
        server.AddFile(
            "/npm/station-demo-plugin/-/station-demo-plugin-2.0.0.zip",
            await new AbsolutePath(npmPackagePath).ReadAllBytesAsync()
        );
        server.Start();

        directory.WriteFile(
            "station.yaml",
            $$"""
            roles:
              - git-role
              - url-role
              - nuget-role
              - npm-role
            plugins:
              - provider: git
                url: {{gitPluginRoot}}
              - provider: url
                url: {{server.BaseUrl}}/packages/url-plugin.zip
              - provider: nuget
                id: station.demo.plugin
                version: 1.2.3
                feed: {{server.BaseUrl}}/nuget/index.json
              - provider: npm
                id: station-demo-plugin
                version: 2.0.0
                registry: {{server.BaseUrl}}/npm
            vars: {}
            """
        );

        var engine = new ConfigEngine();
        var result = await engine.RestoreAsync(directory.Path);

        result.Succeeded.Should().BeTrue();
        result.Plugins.Should().HaveCount(4);
        result
            .Plugins.Select(plugin => plugin.PluginName)
            .Should()
            .BeEquivalentTo(["git.plugin", "url.plugin", "nuget.plugin", "npm.plugin"]);
    }

    [Fact]
    public async Task Validate_resolves_plugin_roles_from_local_plugins()
    {
        using var directory = new TemporaryDirectory();
        PluginFixtureBuilder.CreatePlugin(
            directory,
            "plugins/demo_plugin",
            "example.plugin",
            "plugin-role",
            includeModule: true
        );
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - plugin-role
            plugins:
              - demo_plugin
            vars:
              destination_path: OUTPUT.txt
            """
        );

        var engine = new ConfigEngine();
        var result = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Validated");
    }

    [Fact]
    public async Task Validate_fails_when_short_module_name_is_ambiguous()
    {
        using var directory = new TemporaryDirectory();
        PluginFixtureBuilder.CreatePlugin(
            directory,
            "plugins/demo_plugin",
            "example.plugin",
            "plugin-role",
            includeModule: true
        );
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - base
            plugins:
              - demo_plugin
            vars:
              destination_path: OUTPUT.txt
            """
        );
        directory.WriteFile(
            "roles/base/role.yaml",
            """
            - script:
                path: scripts/test.ps1
                destination: "{{ destination_path }}"
            """
        );
        directory.WriteFile("roles/base/scripts/test.ps1", "Write-Output '{\"changed\":false}'");

        var engine = new ConfigEngine();
        var result = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        result.Succeeded.Should().BeFalse();
        result
            .Diagnostics.Should()
            .ContainSingle(x => x.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explain_lists_tasks_parameters_and_variables()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - base
            plugins: []
            vars:
              greeting: hello
            """
        );
        directory.WriteFile(
            "roles/base/role.yaml",
            """
            - template:
                template: sample
                destination: out/{{ greeting }}.txt
            """
        );
        directory.WriteFile("roles/base/templates/sample.scriban", "{{ greeting }}");

        var engine = new ConfigEngine();
        var result = await engine.ExplainAsync(directory.Path);

        result.Succeeded.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.Children.Should().Contain(x => x.Text == "variables");
        var tasksSection = result.Plan.Children.Single(x => x.Text == "tasks");
        var taskNode = tasksSection.Children.Should().ContainSingle().Subject;
        taskNode.Text.Should().Be("base.tasks.1 [station.builtin.template]");
        taskNode.Children.Should().Contain(x => x.Text == "role: base");
        taskNode
            .Children.Single(x => x.Text == "parameters")
            .Children.Should()
            .Contain(x => x.Text == "template = sample");
        taskNode
            .Children.Single(x => x.Text == "variables")
            .Children.Should()
            .Contain(x => x.Text == "greeting = hello");
    }

    [Fact]
    public async Task Validate_supports_javascript_roles_and_defaults()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - base
            plugins: []
            vars:
              destination_path: OUTPUT.txt
            """
        );
        directory.WriteFile(
            "vars/00-common.js",
            """
            export default {
              source_name: 'sample.txt'
            };
            """
        );
        directory.WriteFile(
            "roles/base/defaults.js",
            """
            export default {
              destination_path: 'DEFAULT.txt'
            };
            """
        );
        directory.WriteFile(
            "roles/base/role.js",
            """
            import { copy } from 'station:modules/builtin';
            import { destination_path, source_name } from 'station:vars';

            task('Copy using JS role', copy, {
              file: source_name,
              destination: destination_path
            });
            """
        );
        directory.WriteFile("roles/base/files/sample.txt", "hello");

        var engine = new ConfigEngine();
        var result = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Validated");
    }

    [Fact]
    public async Task Validate_supports_javascript_variable_references_inside_expressions()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - base
            plugins: []
            vars:
              user_home: C:/Users/demo
            """
        );
        directory.WriteFile(
            "roles/base/role.js",
            """
            import { copy } from 'station:modules/builtin';
            import { user_home } from 'station:vars';

            task('Copy using JS expression', copy, {
              file: 'sample.txt',
              destination: `${user_home}/.config/my_config.json`
            });
            """
        );
        directory.WriteFile("roles/base/files/sample.txt", "hello");

        var engine = new ConfigEngine();
        var result = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Validated");
    }

    [Fact]
    public async Task Validate_resolves_plugin_roles_from_local_plugins_authored_in_javascript()
    {
        using var directory = new TemporaryDirectory();
        PluginFixtureBuilder.CreatePlugin(
            directory,
            "plugins/demo_plugin",
            "example.plugin",
            "plugin-role",
            includeModule: true,
            useJavaScriptRole: true
        );
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - plugin-role
            plugins:
              - demo_plugin
            vars:
              destination_path: OUTPUT.txt
            """
        );

        var engine = new ConfigEngine();
        var result = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Validated");
    }

    private static async Task InitializeGitRepositoryAsync(string root)
    {
        await RunGitAsync(root, "init");
        await RunGitAsync(root, "config", "user.email", "station-tests@example.invalid");
        await RunGitAsync(root, "config", "user.name", "Station Tests");
        await RunGitAsync(root, "add", ".");
        await RunGitAsync(root, "commit", "-m", "Initial plugin");
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, await process.StandardError.ReadToEndAsync());
    }
}
