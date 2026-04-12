using Station.Abstractions.Models;
using Station.Core.Configuration;
using Station.Core.Configuration.Services;
using Station.Tests.Helpers;

namespace Station.Tests.Configuration;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_applies_variable_precedence_and_parses_root_and_role_tasks()
    {
        using var directory = new TemporaryDirectory();
        var originalValue = Environment.GetEnvironmentVariable("CONFIG_TOOL_VAR_GREETING");

        try
        {
            Environment.SetEnvironmentVariable("CONFIG_TOOL_VAR_GREETING", "env");

            WriteFile(directory.Path, "station.yaml", """
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
                """);
            WriteFile(directory.Path, "vars/00-common.yaml", """
                greeting: vars
                """);
            WriteFile(directory.Path, "roles/base/defaults.yaml", """
                greeting: default
                """);
            WriteFile(directory.Path, "roles/base/role.yaml", """
                - template:
                    template: test
                    destination: role/{{ greeting }}.txt
                """);

            var cliVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["greeting"] = "cli",
            };

            var loader = new ConfigLoader();
            var configuration = loader.Load(directory.Path, cliVariables);

            Assert.Equal("cli", configuration.RootVariables.Get<string>("greeting"));
            Assert.Equal(["base"], configuration.RequestedRoles);

            var plugin = Assert.Single(configuration.PluginDeclarations);
            Assert.Equal("local", plugin.Provider);
            Assert.Equal("demo_plugin.zip", plugin.Name);

            var rootTask = Assert.Single(configuration.RootTasks);
            Assert.Equal("station.tasks.1", rootTask.Name);
            Assert.Equal("output/{{ greeting }}.txt", rootTask.Parameters["destination"].GetValue<string>());

            var role = Assert.Single(loader.DiscoverLocalRoles(directory.Path));
            var roleTask = Assert.Single(loader.LoadRoleTasks(role, configuration.RootVariables));
            Assert.Equal("base.tasks.1", roleTask.Name);
            Assert.Equal("role/{{ greeting }}.txt", roleTask.Parameters["destination"].GetValue<string>());
            Assert.Equal("cli", roleTask.Variables.Get<string>("greeting"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONFIG_TOOL_VAR_GREETING", originalValue);
        }
    }

    [Fact]
    public void Load_supports_javascript_vars_defaults_and_roles()
    {
        using var directory = new TemporaryDirectory();
        WriteFile(directory.Path, "station.yaml", """
            roles:
              - base
            plugins: []
            vars:
              greeting: root
              destination_path: root-output.txt
            """);
        WriteFile(directory.Path, "vars/00-common.yaml", """
            greeting: yaml
            """);
        WriteFile(directory.Path, "vars/10-derived.js", """
            import { greeting } from 'station:vars';

            export default {
              chosen_greeting: greeting
            };
            """);
        WriteFile(directory.Path, "roles/base/defaults.js", """
            export default {
              destination_path: 'defaults-output.txt'
            };
            """);
        WriteFile(directory.Path, "roles/base/role.js", """
            import { copy } from 'station:modules/builtin';
            import { destination_path } from 'station:vars';

            task('Copy from JS role', copy, {
              file: 'sample.txt',
              destination: destination_path
            });
            """);

        var loader = new ConfigLoader();
        var configuration = loader.Load(directory.Path);

        Assert.Equal("root", configuration.RootVariables.Get<string>("greeting"));
        Assert.Equal("root", configuration.RootVariables.Get<string>("chosen_greeting"));

        var role = Assert.Single(loader.DiscoverLocalRoles(directory.Path));
        var roleTask = Assert.Single(loader.LoadRoleTasks(role, configuration.RootVariables));
        Assert.Equal("Copy from JS role", roleTask.Name);
        var destination = Assert.IsType<VariableReference>(roleTask.Parameters["destination"]);
        Assert.Equal("destination_path", destination.Path);
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents.Replace("\r\n", "\n"));
    }
}
