namespace Station.Testing;

public static class PluginFixtureBuilder
{
    public static string CreatePlugin(
        TemporaryDirectory root,
        string pluginDirectoryName,
        string pluginName,
        string roleName,
        bool includeModule = false,
        bool useJavaScriptRole = false)
    {
        var pluginRoot = root.GetPath(pluginDirectoryName);
        Directory.CreateDirectory(pluginRoot);

        var moduleList = includeModule ? "  - script" : string.Empty;
        root.WriteFile($"{pluginDirectoryName}/plugin-manifest.yaml", $$"""
            name: {{pluginName}}
            version: 1.0.0
            roles:
              - {{roleName}}
            modules:
            {{moduleList}}
            """);

        var moduleReference = includeModule ? $"{pluginName}.script" : "script";
        var roleContents = useJavaScriptRole
            ? """
            import { script } from 'station:modules/builtin';

            task('plugin script', '__MODULE_REFERENCE__', {
              path: 'scripts/sample.ps1'
            });
            """.Replace("__MODULE_REFERENCE__", moduleReference, StringComparison.Ordinal)
            : """
            - __MODULE_REFERENCE__:
                path: scripts/sample.ps1
            """.Replace("__MODULE_REFERENCE__", moduleReference, StringComparison.Ordinal);
        root.WriteFile(
            $"{pluginDirectoryName}/roles/{roleName}/{(useJavaScriptRole ? "role.js" : "role.yaml")}",
            roleContents);
        root.WriteFile(
            $"{pluginDirectoryName}/roles/{roleName}/scripts/sample.ps1",
            "Write-Output '{\"changed\":false,\"message\":\"ok\"}'");

        if (includeModule)
        {
            var modulePath = typeof(Modules.Builtin.Script.ScriptModule).Assembly.Location;
            var destinationPath = Path.Combine(pluginRoot, "modules", "script", "script.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(modulePath, destinationPath, overwrite: true);
        }

        return pluginRoot;
    }
}
