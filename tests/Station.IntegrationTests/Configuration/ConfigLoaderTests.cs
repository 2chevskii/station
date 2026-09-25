using FluentAssertions;
using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Artifacts;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Testing;

namespace Station.IntegrationTests.Configuration;

[Collection("Environment variables")]
public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_applies_variable_precedence_and_parses_root_and_role_tasks()
    {
        using var directory = new TemporaryDirectory();
        using var environment = new EnvironmentVariableScope("CONFIG_TOOL_VAR_GREETING", "env");

        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - base
            plugins:
              - demo_plugin.zip
            vars:
              greeting: root
            tasks:
              - template:
                  template: root
                  destination: output/{{ greeting }}.txt
            """
        );
        directory.WriteFile(
            "vars/00-common.yaml",
            """
            greeting: vars
            """
        );
        directory.WriteFile(
            "roles/base/defaults.yaml",
            """
            greeting: default
            """
        );
        directory.WriteFile(
            "roles/base/role.yaml",
            """
            - template:
                template: test
                destination: role/{{ greeting }}.txt
            """
        );

        var loader = new ConfigLoader();
        var configuration = loader.Load(
            directory.Path,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["greeting"] = "cli",
            }
        );

        configuration.RootVariables.Get<string>("greeting").Should().Be("cli");
        configuration.RequestedRoles.Should().Equal("base");
        configuration
            .PluginDeclarations.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { Provider = "local", Name = "demo_plugin.zip" });

        var rootTask = configuration.RootTasks.Should().ContainSingle().Subject;
        rootTask.Name.Should().Be("station.tasks.1");
        rootTask
            .Parameters["destination"]
            .GetValue<string>()
            .Should()
            .Be("output/{{ greeting }}.txt");

        var role = loader.DiscoverLocalRoles(directory.Path).Should().ContainSingle().Subject;
        var roleTask = loader
            .LoadRoleTasks(role, configuration.RootVariables)
            .Should()
            .ContainSingle()
            .Subject;
        roleTask.Name.Should().Be("base.tasks.1");
        roleTask
            .Parameters["destination"]
            .GetValue<string>()
            .Should()
            .Be("role/{{ greeting }}.txt");
        roleTask.Variables.Get<string>("greeting").Should().Be("cli");
    }

    [Fact]
    public void Load_supports_javascript_vars_defaults_and_roles()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "station.yaml",
            """
            roles:
              - base
            plugins: []
            vars:
              greeting: root
              destination_path: root-output.txt
            """
        );
        directory.WriteFile(
            "vars/00-common.yaml",
            """
            greeting: yaml
            """
        );
        directory.WriteFile(
            "vars/10-derived.js",
            """
            import { greeting } from 'station:vars';

            export default {
              chosen_greeting: greeting
            };
            """
        );
        directory.WriteFile(
            "roles/base/defaults.js",
            """
            export default {
              destination_path: 'defaults-output.txt'
            };
            """
        );
        directory.WriteFile(
            "roles/base/role.js",
            """
            import { copy } from 'station:modules/builtin';
            import { destination_path } from 'station:vars';

            task('Copy from JS role', copy, {
              file: 'sample.txt',
              destination: destination_path
            });
            """
        );

        var loader = new ConfigLoader();
        var configuration = loader.Load(directory.Path);

        configuration.RootVariables.Get<string>("greeting").Should().Be("root");
        configuration.RootVariables.Get<string>("chosen_greeting").Should().Be("root");

        var role = loader.DiscoverLocalRoles(directory.Path).Should().ContainSingle().Subject;
        var roleTask = loader
            .LoadRoleTasks(role, configuration.RootVariables)
            .Should()
            .ContainSingle()
            .Subject;
        roleTask.Name.Should().Be("Copy from JS role");
        roleTask
            .Parameters["destination"]
            .Should()
            .BeOfType<VariableReference>()
            .Which.Path.Should()
            .Be("destination_path");
    }

    [Fact]
    public void Load_supports_javascript_variable_references_inside_expressions()
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

            task('Copy from JS expression', copy, {
              file: 'sample.txt',
              destination: `${user_home}/.config/my_config.json`
            });
            """
        );

        var loader = new ConfigLoader();
        var configuration = loader.Load(directory.Path);
        var role = loader.DiscoverLocalRoles(directory.Path).Should().ContainSingle().Subject;
        var roleTask = loader
            .LoadRoleTasks(role, configuration.RootVariables)
            .Should()
            .ContainSingle()
            .Subject;

        roleTask
            .Parameters["destination"]
            .Should()
            .BeOfType<TemplateMarkup>()
            .Which.TemplateText.Should()
            .Be("{{ user_home }}/.config/my_config.json");
    }
}
