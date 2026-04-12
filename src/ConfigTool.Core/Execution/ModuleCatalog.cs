using System.Reflection;
using System.Runtime.Loader;
using ConfigTool.Abstractions;
using ConfigTool.Modules.Apt;
using ConfigTool.Modules.Dotfile;
using ConfigTool.Modules.Script;
using ConfigTool.Modules.Scoop;
using ConfigTool.Modules.Winget;

namespace ConfigTool.Core;

internal sealed class ModuleCatalog
{
    private readonly IReadOnlyDictionary<string, ResolvedModule> modulesByFullName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ResolvedModule>> modulesByShortName;

    private ModuleCatalog(
        IReadOnlyDictionary<string, ResolvedModule> modulesByFullName,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedModule>> modulesByShortName)
    {
        this.modulesByFullName = modulesByFullName;
        this.modulesByShortName = modulesByShortName;
    }

    public static ModuleCatalog Create(
        IEnumerable<ModuleRegistration>? additionalBuiltInModules,
        IEnumerable<string> localModulePaths,
        IEnumerable<ResolvedPluginManifest> plugins)
    {
        var resolvedModules = new List<ResolvedModule>();

        foreach (var registration in CreateDefaultBuiltIns().Concat(additionalBuiltInModules ?? []))
        {
            ValidateShortName(registration.ShortName, $"built-in module '{registration.ShortName}'");
            if (!string.Equals(registration.Module.Descriptor.Name, registration.ShortName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigurationException(
                    $"Built-in module registration '{registration.ProviderName}.{registration.ShortName}' does not match descriptor name '{registration.Module.Descriptor.Name}'.");
            }

            resolvedModules.Add(new ResolvedModule(
                registration.ProviderName,
                registration.ShortName,
                $"{registration.ProviderName}.{registration.ShortName}",
                registration.Module));
        }

        foreach (var modulePath in localModulePaths)
        {
            var shortName = Path.GetFileNameWithoutExtension(modulePath);
            resolvedModules.Add(LoadModuleFromAssembly(modulePath, "configtool.local", shortName));
        }

        foreach (var plugin in plugins)
        {
            foreach (var shortName in plugin.Modules)
            {
                var modulePath = Path.Combine(plugin.PluginRoot, "modules", shortName, $"{shortName}.dll");
                resolvedModules.Add(LoadModuleFromAssembly(modulePath, plugin.Name, shortName));
            }
        }

        var modulesByFullName = resolvedModules.ToDictionary(
            module => module.FullName,
            StringComparer.OrdinalIgnoreCase);
        var modulesByShortName = resolvedModules
            .GroupBy(module => module.ShortName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ResolvedModule>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return new ModuleCatalog(modulesByFullName, modulesByShortName);
    }

    private static IReadOnlyList<ModuleRegistration> CreateDefaultBuiltIns() =>
    [
        new("configtool.builtin", "apt", new AptModule()),
        new("configtool.builtin", "dotfile", new DotfileModule(TimeProvider.System)),
        new("configtool.builtin", "script", new ScriptModule()),
        new("configtool.builtin", "scoop", new ScoopModule()),
        new("configtool.builtin", "winget", new WingetModule()),
    ];

    public ResolvedModule Resolve(string name)
    {
        if (modulesByFullName.TryGetValue(name, out var fullNameMatch))
        {
            return fullNameMatch;
        }

        if (!modulesByShortName.TryGetValue(name, out var shortNameMatches))
        {
            throw new ConfigurationException($"Module '{name}' is not registered.");
        }

        if (shortNameMatches.Count > 1)
        {
            throw new ConfigurationException(
                $"Module '{name}' is ambiguous. Use one of: {string.Join(", ", shortNameMatches.Select(static module => module.FullName))}.");
        }

        return shortNameMatches[0];
    }

    private static ResolvedModule LoadModuleFromAssembly(string assemblyPath, string providerName, string shortName)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new ConfigurationException($"Module assembly '{assemblyPath}' does not exist.");
        }

        ValidateShortName(shortName, $"module '{shortName}'");

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        var discoveredModules = assembly
            .GetExportedTypes()
            .Where(static type => type is { IsAbstract: false, IsInterface: false } &&
                typeof(IConfigModule).IsAssignableFrom(type))
            .ToArray();

        if (discoveredModules.Length != 1)
        {
            throw new ConfigurationException(
                $"Module assembly '{assemblyPath}' must contain exactly one public {nameof(IConfigModule)} implementation.");
        }

        if (Activator.CreateInstance(discoveredModules[0]) is not IConfigModule module)
        {
            throw new ConfigurationException(
                $"Module '{discoveredModules[0].FullName}' in '{assemblyPath}' must expose a public parameterless constructor.");
        }

        if (!string.Equals(module.Descriptor.Name, shortName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(
                $"Module assembly '{assemblyPath}' is expected to export '{shortName}', but its descriptor name is '{module.Descriptor.Name}'.");
        }

        return new ResolvedModule(providerName, shortName, $"{providerName}.{shortName}", module);
    }

    private static void ValidateShortName(string shortName, string owner)
    {
        if (string.IsNullOrWhiteSpace(shortName) ||
            shortName.Contains('.') ||
            shortName.Contains(Path.DirectorySeparatorChar) ||
            shortName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ConfigurationException($"{owner} must have a valid short name.");
        }
    }
}
