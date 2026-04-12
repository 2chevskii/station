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
            Assert.Equal("output/{{ greeting }}.txt", rootTask.Parameters["destination"]?.GetValue<string>());

            var role = Assert.Single(loader.DiscoverLocalRoles(directory.Path));
            var roleTask = Assert.Single(loader.LoadRoleTasks(role, configuration.RootVariables));
            Assert.Equal("base.tasks.1", roleTask.Name);
            Assert.Equal("role/{{ greeting }}.txt", roleTask.Parameters["destination"]?.GetValue<string>());
            Assert.Equal("cli", roleTask.Variables.Get<string>("greeting"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONFIG_TOOL_VAR_GREETING", originalValue);
        }
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents.Replace("\r\n", "\n"));
    }
}
