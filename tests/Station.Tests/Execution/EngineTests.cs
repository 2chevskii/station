using Station.Abstractions.Enums;
using Station.Core.Configuration;
using Station.Core.Execution;
using Station.Core.Infrastructure.Templates;
using Station.Core.Plugins;
using Station.Modules.Builtin.Dotfile;
using Station.Modules.Builtin.Script;
using Station.Tests.Helpers;

namespace Station.Tests.Execution;

public sealed class EngineTests
{
    [Fact]
    public async Task Restore_returns_plugin_records_for_local_plugins()
    {
        using var directory = new TemporaryDirectory();
        CreateLocalPlugin(directory.Path, "demo_plugin", "example.plugin", "plugin-role", includeModule: false);
        WriteFile(directory.Path, "station.yaml", """
            roles:
              - plugin-role
            plugins:
              - demo_plugin
            vars: {}
            """);

        var engine = CreateEngine();
        var result = await engine.RestoreAsync(directory.Path);

        Assert.True(result.Succeeded);
        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("example.plugin", plugin.PluginName);
        Assert.True(plugin.IsLocal);
        Assert.True(plugin.CacheHit);
    }

    [Fact]
    public async Task Validate_resolves_plugin_roles_from_local_plugins()
    {
        using var directory = new TemporaryDirectory();
        CreateLocalPlugin(directory.Path, "demo_plugin", "example.plugin", "plugin-role", includeModule: true);
        WriteFile(directory.Path, "station.yaml", """
            roles:
              - plugin-role
            plugins:
              - demo_plugin
            vars:
              destination_path: OUTPUT.txt
            """);

        var engine = CreateEngine();
        var result = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        Assert.True(result.Succeeded);
        Assert.Contains("Validated", result.Message);
    }

    [Fact]
    public async Task Validate_fails_when_short_module_name_is_ambiguous()
    {
        using var directory = new TemporaryDirectory();
        CreateLocalPlugin(directory.Path, "demo_plugin", "example.plugin", "plugin-role", includeModule: true);
        WriteFile(directory.Path, "station.yaml", """
            roles:
              - base
            plugins:
              - demo_plugin
            vars:
              destination_path: OUTPUT.txt
            """);
        WriteFile(directory.Path, "roles/base/role.yaml", """
            - script:
                path: scripts/test.ps1
                destination: "{{ destination_path }}"
            """);
        WriteFile(directory.Path, "roles/base/scripts/test.ps1", "Write-Output '{\"changed\":false}'");

        var engine = CreateEngine();
        var result = await engine.RunAsync(directory.Path, ExecutionMode.Validate);

        Assert.False(result.Succeeded);
        Assert.Contains("ambiguous", result.Diagnostics.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explain_lists_tasks_parameters_and_variables()
    {
        using var directory = new TemporaryDirectory();
        WriteFile(directory.Path, "station.yaml", """
            roles:
              - base
            plugins: []
            vars:
              greeting: hello
            """);
        WriteFile(directory.Path, "roles/base/role.yaml", """
            - template:
                template: sample
                destination: out/{{ greeting }}.txt
            """);
        WriteFile(directory.Path, "roles/base/templates/sample.scriban", "{{ greeting }}");

        var engine = CreateEngine();
        var result = await engine.ExplainAsync(directory.Path);

        Assert.True(result.Succeeded);
        Assert.Contains("base.tasks.1 [station.builtin.template]", result.Diagram);
        Assert.Contains("template = sample", result.Diagram);
        Assert.Contains("greeting = hello", result.Diagram);
    }

    private static ConfigEngine CreateEngine()
    {
        var processRunner = new StubProcessRunner();
        var configLoader = new ConfigLoader(new ScribanTemplateRenderer());
        var pluginRestorer = new PluginRestorer(configLoader, processRunner);

        return new ConfigEngine(
            configLoader,
            pluginRestorer,
            new StubPrivilegeDetector(isElevated: true),
            []);
    }

    private static void CreateLocalPlugin(
        string root,
        string declarationName,
        string pluginName,
        string roleName,
        bool includeModule)
    {
        var pluginRoot = Path.Combine(root, "plugins", declarationName);
        Directory.CreateDirectory(pluginRoot);

        var moduleList = includeModule ? "  - script" : string.Empty;
        WriteFile(pluginRoot, "plugin-manifest.yaml", $$"""
            name: {{pluginName}}
            version: 1.0.0
            roles:
              - {{roleName}}
            modules:
            {{moduleList}}
            """);
        WriteFile(
            pluginRoot,
            $"roles/{roleName}/role.yaml",
            """
            - __PLUGIN_NAME__.script:
                path: scripts/sample.ps1
            """.Replace("__PLUGIN_NAME__", pluginName, StringComparison.Ordinal)
        );
        WriteFile(pluginRoot, $"roles/{roleName}/scripts/sample.ps1", "Write-Output '{\"changed\":false}'");

        if (includeModule)
        {
            var modulePath = typeof(ScriptModule).Assembly.Location;
            var destinationPath = Path.Combine(pluginRoot, "modules", "script", "script.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(modulePath, destinationPath, overwrite: true);
        }
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents.Replace("\r\n", "\n"));
    }
}
