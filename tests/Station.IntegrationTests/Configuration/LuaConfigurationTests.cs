using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Station.Abstractions.Configuration;
using Station.Abstractions.Execution;
using Station.Core.Configuration.Loading;
using Station.Core.Execution.Running;
using Station.IntegrationTests.Infrastructure;
using Station.Testing;

namespace Station.IntegrationTests.Configuration;

[Collection("Environment variables")]
public sealed class LuaConfigurationTests
{
    [Fact]
    public void Load_merges_lua_yaml_and_javascript_variables_and_preserves_task_references()
    {
        using var directory = new TemporaryDirectory();
        using var environment = new EnvironmentVariableScope("CONFIG_TOOL_VAR_GREETING", "env");
        directory.WriteFile("station.yaml", "roles: [base]\nvars:\n  greeting: root\n");
        directory.WriteFile("vars/00-common.yaml", "greeting: yaml\n");
        directory.WriteFile("vars/10-js.js", "export default { from_js: 'JavaScript' };");
        directory.WriteFile(
            "vars/20-lua.lua",
            """
            return {
                greeting = 'Lua',
                chosen_greeting = vars.greeting,
                chosen_js = vars.from_js,
                nested = { label = 'Привет' },
                enabled = true,
                count = 42,
                fraction = 1.5,
                items = { 'first', 'second' },
                empty_list = array(),
                empty_mapping = {}
            }
            """
        );
        directory.WriteFile(
            "roles/base/defaults.lua",
            "return { greeting = 'default', destination = 'default.txt' }"
        );
        directory.WriteFile(
            "roles/base/role.lua",
            """
            task('Задача Lua', modules.copy, { file = vars.nested.label, destination = vars.destination })
            task('example.plugin.custom', { destination = vars.destination .. '/output.txt' })
            """
        );

        using var services = IntegrationServices.Create();
        var loader = services.GetRequiredService<ConfigLoader>();
        var configuration = loader.Load(
            directory.Path,
            new Dictionary<string, string> { ["greeting"] = "cli" }
        );
        var variables = configuration.RootVariables;
        variables.Get<string>("greeting").Should().Be("cli");
        variables.Get<string>("chosen_greeting").Should().Be("cli");
        variables.Get<string>("from_js").Should().Be("JavaScript");
        variables.Get<string>("chosen_js").Should().Be("JavaScript");
        variables.Get<string>("nested.label").Should().Be("Привет");
        variables.Get<bool>("enabled").Should().BeTrue();
        variables.Get<long>("count").Should().Be(42);
        variables.Get<double>("fraction").Should().Be(1.5);
        variables.Get<string[]>("items").Should().Equal("first", "second");
        variables.Root["empty_list"].Should().BeOfType<ConfigArray>().Which.Should().BeEmpty();
        variables.Root["empty_mapping"].Should().BeOfType<ConfigObject>().Which.Should().BeEmpty();

        var role = loader.DiscoverLocalRoles(directory.Path).Should().ContainSingle().Subject;
        var tasks = loader.LoadRoleTasks(role, variables);
        tasks.Should().HaveCount(2);
        tasks[0].Name.Should().Be("Задача Lua");
        tasks[0]
            .Parameters["file"]
            .Should()
            .BeOfType<VariableReference>()
            .Which.Path.Should()
            .Be("nested.label");
        tasks[0]
            .Parameters["destination"]
            .Should()
            .BeOfType<VariableReference>()
            .Which.Path.Should()
            .Be("destination");
        tasks[0].Variables.Get<string>("destination").Should().Be("default.txt");
        tasks[1].ModuleName.Should().Be("example.plugin.custom");
        tasks[1].Name.Should().Be("base.tasks.2");
        tasks[1]
            .Parameters["destination"]
            .Should()
            .BeOfType<TemplateMarkup>()
            .Which.TemplateText.Should()
            .Be("{{ destination }}/output.txt");
    }

    [Theory]
    [InlineData("return 42", "top-level mapping")]
    [InlineData("return { 'one' }", "top-level mapping")]
    [InlineData("return nil", "top-level mapping")]
    [InlineData("local value = 1", "exactly one table")]
    [InlineData("return {}, {}", "exactly one table")]
    [InlineData("return { [2] = 'gap' }", "consecutive integer keys")]
    [InlineData("return { [1] = 'one', label = 'mixed' }", "consecutive integer keys")]
    [InlineData("return { [true] = 'invalid' }", "consecutive integer keys")]
    [InlineData("return { callback = function() end }", "Unsupported configuration value")]
    [InlineData("return { Name = 1, name = 2 }", "duplicate mapping key")]
    [InlineData("local value = {}; value.self = value; return value", "cyclic table")]
    [InlineData("return { broken = ", "Could not evaluate Lua")]
    [InlineData("while true do end", "evaluation limit exceeded")]
    public void Load_rejects_invalid_lua_variables_with_source_context(
        string source,
        string expectedMessage
    )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("station.yaml", "roles: [base]\n");
        directory.WriteFile("roles/base/role.lua", "");
        directory.WriteFile("vars/invalid.lua", source);
        using var services = IntegrationServices.Create();

        var action = () => services.GetRequiredService<ConfigLoader>().Load(directory.Path);
        var exception = action.Should().Throw<ConfigurationException>().Which;
        exception.Message.Should().Contain("vars/invalid.lua").And.Contain(expectedMessage);
    }

    [Theory]
    [InlineData("task()", "requires a module and parameters")]
    [InlineData("task('', {})", "nonempty module name")]
    [InlineData("task('copy', 1)", "parameters table")]
    [InlineData("task('', 'copy', {})", "nonempty name")]
    [InlineData("task('copy', { 'invalid' })", "must map to an object")]
    [InlineData("return {}", "without returning values")]
    public void Load_rejects_invalid_lua_role_tasks(string source, string expectedMessage)
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("station.yaml", "roles: [base]\n");
        directory.WriteFile("roles/base/role.lua", source);
        using var services = IntegrationServices.Create();
        var loader = services.GetRequiredService<ConfigLoader>();
        var configuration = loader.Load(directory.Path);
        var role = loader.DiscoverLocalRoles(directory.Path).Single();

        var action = () => loader.LoadRoleTasks(role, configuration.RootVariables);
        action.Should().Throw<ConfigurationException>().WithMessage($"*{expectedMessage}*");
    }

    [Theory]
    [InlineData("role.yaml", "[]", "role.lua", "")]
    [InlineData("role.js", "", "role.lua", "")]
    [InlineData("defaults.yaml", "{}", "defaults.lua", "return {}")]
    [InlineData("defaults.js", "export default {};", "defaults.lua", "return {}")]
    [InlineData("vars/example.yaml", "{}", "vars/example.lua", "return {}")]
    [InlineData("vars/example.js", "export default {};", "vars/example.lua", "return {}")]
    public void Load_rejects_conflicting_formats(
        string firstFile,
        string firstContents,
        string secondFile,
        string secondContents
    )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("station.yaml", "roles: [base]\n");
        directory.WriteFile("roles/base/role.lua", "");
        var prefix = firstFile.StartsWith("vars/", StringComparison.Ordinal) ? "" : "roles/base/";
        directory.WriteFile(prefix + firstFile, firstContents);
        directory.WriteFile(prefix + secondFile, secondContents);
        using var services = IntegrationServices.Create();
        var loader = services.GetRequiredService<ConfigLoader>();

        var action = () =>
        {
            var configuration = loader.Load(directory.Path);
            var role = loader.DiscoverLocalRoles(directory.Path).Single();
            loader.LoadRoleTasks(role, configuration.RootVariables);
        };
        var exception = action.Should().Throw<ConfigurationException>().Which;
        exception
            .Message.Should()
            .Contain(Path.GetFileName(firstFile))
            .And.Contain(Path.GetFileName(secondFile));
    }

    [Fact]
    public async Task Apply_executes_lua_plugin_roles_and_resolves_references_after_overrides()
    {
        using var directory = new TemporaryDirectory();
        var outputPath = Path.Combine(directory.Path, "actual.txt").Replace('\\', '/');
        directory.WriteFile(
            "station.yaml",
            $"""
            roles: [example.plugin.base]
            plugins: [demo]
            vars:
              output_name: actual.txt
              output_directory: '{directory.Path.Replace( '\\', '/' )}'
            """
        );
        directory.WriteFile(
            "plugins/demo/manifest.yaml",
            "name: example.plugin\nversion: 1.0.0\nroles: [base]\n"
        );
        directory.WriteFile("vars/common.lua", "return { source_name = 'sample.txt' }");
        directory.WriteFile(
            "plugins/demo/roles/base/defaults.lua",
            """
            return {
                output_name = 'default.txt',
                destination = ref('output_directory') .. '/' .. ref('output_name')
            }
            """
        );
        directory.WriteFile(
            "plugins/demo/roles/base/role.lua",
            """
            local name = require('name')
            task(name, modules.copy, { file = vars.source_name, destination = vars.destination })
            """
        );
        directory.WriteFile("plugins/demo/roles/base/name.lua", "return 'Copy with Lua'");
        directory.WriteFile("plugins/demo/roles/base/files/sample.txt", "Привет из Lua");
        using var services = IntegrationServices.Create();

        var result = await services
            .GetRequiredService<ConfigEngine>()
            .RunAsync(directory.Path, ExecutionMode.Apply);

        result.Succeeded.Should().BeTrue(result.Message);
        File.ReadAllText(outputPath).Should().Be("Привет из Lua");
    }
}
