namespace Station.Core.Configuration.Plugins;

internal sealed class PluginModuleExportParser
{
    public IReadOnlyList<PluginModuleExport> Parse(
        IEnumerable<string>? declarations,
        string pluginRoot,
        string manifestPath
    )
    {
        var exports = new List<PluginModuleExport>();
        foreach (var declaration in declarations ?? [])
        {
            var value = declaration.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var separatorIndex = value.IndexOf(':');
            if (separatorIndex < 0)
            {
                ValidateLegacyName(value, manifestPath);
                exports.Add(
                    new PluginModuleExport(
                        ResolveAssemblyPath($"modules/{value}/{value}.dll", pluginRoot, manifestPath),
                        TypeName: null,
                        ExpectedName: value
                    )
                );
                continue;
            }

            var assemblyPath = value[..separatorIndex].Trim();
            var typeName = value[(separatorIndex + 1)..].Trim();
            if (
                string.IsNullOrWhiteSpace(assemblyPath)
                || string.IsNullOrWhiteSpace(typeName)
                || typeName.Contains(':')
            )
            {
                throw new ConfigurationException(
                    $"Plugin manifest '{manifestPath}' contains invalid module export '{value}'. Expected 'path/to/assembly.dll:Namespace.ModuleClass'."
                );
            }

            exports.Add(
                new PluginModuleExport(
                    ResolveAssemblyPath(assemblyPath, pluginRoot, manifestPath),
                    typeName,
                    ExpectedName: null
                )
            );
        }

        return exports.Distinct().ToArray();
    }

    private static void ValidateLegacyName(string name, string manifestPath)
    {
        if (name.Contains('.') || Path.GetFileName(name) != name || name.Contains('\\'))
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' contains invalid module export '{name}'. Legacy exports must be short names."
            );
        }
    }

    private static string ResolveAssemblyPath(
        string relativePath,
        string pluginRoot,
        string manifestPath
    )
    {
        try
        {
            return ResolveAssemblyPathCore(relativePath, pluginRoot, manifestPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' contains invalid module assembly path '{relativePath}'."
            );
        }
    }

    private static string ResolveAssemblyPathCore(
        string relativePath,
        string pluginRoot,
        string manifestPath
    )
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        if (
            Path.IsPathRooted(normalizedPath)
            || !string.Equals(Path.GetExtension(normalizedPath), ".dll", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' contains invalid module assembly path '{relativePath}'."
            );
        }

        var fullPath = Path.GetFullPath(Path.Combine(pluginRoot, normalizedPath));
        var pathFromRoot = Path.GetRelativePath(pluginRoot, fullPath);
        if (
            pathFromRoot is "." or ".."
            || pathFromRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(pathFromRoot)
        )
        {
            throw new ConfigurationException(
                $"Plugin manifest '{manifestPath}' contains module assembly path '{relativePath}' outside the plugin root."
            );
        }

        if (!File.Exists(fullPath))
        {
            throw new ConfigurationException($"Module assembly '{fullPath}' does not exist.");
        }

        return fullPath;
    }
}
