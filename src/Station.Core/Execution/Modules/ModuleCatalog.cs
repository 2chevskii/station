using System.Runtime.Loader;
using Station.Abstractions.FileSystem;
using Station.Core.Configuration.Discovery;
using Station.Core.Configuration.Loading;
using Station.Core.Configuration.Plugins;
using Station.Core.Configuration.Roles;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Modules.Builtin.Apt;
using Station.Modules.Builtin.Dotfile;
using Station.Modules.Builtin.Scoop;
using Station.Modules.Builtin.Script;
using Station.Modules.Builtin.Winget;
using Station.Plugins.Abstractions.Manifests;
using Station.Plugins.Abstractions.Sources;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Execution.Modules;

internal sealed class ModuleCatalog
{
    private readonly IReadOnlyDictionary<string, ResolvedModule> modulesByFullName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ResolvedModule>> modulesByShortName;

    private ModuleCatalog(
        IReadOnlyDictionary<string, ResolvedModule> modulesByFullName,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedModule>> modulesByShortName
    )
    {
        this.modulesByFullName = modulesByFullName;
        this.modulesByShortName = modulesByShortName;
    }

    public static ModuleCatalog Create(
        IEnumerable<ModuleRegistration>? additionalBuiltInModules,
        IEnumerable<string> localModulePaths,
        IEnumerable<ResolvedPluginManifest> plugins
    )
    {
        var resolvedModules = new List<ResolvedModule>();

        foreach (var registration in CreateDefaultBuiltIns().Concat(additionalBuiltInModules ?? []))
        {
            var schema = ModuleSchemaRegistry.Get(registration.ModuleType);
            ValidateShortName(schema.Name, $"built-in module '{schema.Name}'");

            resolvedModules.Add(
                new ResolvedModule(
                    registration.ProviderName,
                    schema.Name,
                    $"{registration.ProviderName}.{schema.Name}",
                    schema
                )
            );
        }

        foreach (var modulePath in localModulePaths)
        {
            var shortName = modulePath.AsLocalPath().GetFilenameWithoutExtension();
            resolvedModules.Add(LoadModuleFromAssembly(modulePath, "station.local", shortName));
        }

        foreach (var plugin in plugins)
        {
            foreach (var shortName in plugin.Modules)
            {
                var modulePath = (
                    plugin.PluginRoot.AsAbsolutePath()
                    / PluginLayout.ModulesDirectoryName
                    / shortName
                    / $"{shortName}.dll"
                ).Value;
                resolvedModules.Add(LoadModuleFromAssembly(modulePath, plugin.Name, shortName));
            }
        }

        var modulesByFullName = resolvedModules.ToDictionary(
            module => module.FullName,
            StringComparer.OrdinalIgnoreCase
        );
        var modulesByShortName = resolvedModules
            .GroupBy(module => module.ShortName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ResolvedModule>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase
            );

        return new ModuleCatalog(modulesByFullName, modulesByShortName);
    }

    private static IReadOnlyList<ModuleRegistration> CreateDefaultBuiltIns() =>
        [
            new("station.builtin", typeof(AptModule)),
            new("station.builtin", typeof(CopyModule)),
            new("station.builtin", typeof(TemplateModule)),
            new("station.builtin", typeof(ScriptModule)),
            new("station.builtin", typeof(ScoopModule)),
            new("station.builtin", typeof(WingetModule)),
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
                $"Module '{name}' is ambiguous. Use one of: {string.Join(", ", shortNameMatches.Select(static module => module.FullName))}."
            );
        }

        return shortNameMatches[0];
    }

    private static ResolvedModule LoadModuleFromAssembly(
        string assemblyPath,
        string providerName,
        string shortName
    )
    {
        var resolvedPath = assemblyPath.AsLocalPath().ResolveToCurrentDirectory();
        if (!resolvedPath.ExistsFile())
        {
            throw new ConfigurationException($"Module assembly '{assemblyPath}' does not exist.");
        }

        ValidateShortName(shortName, $"module '{shortName}'");

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(resolvedPath.Value);
        var discoveredModules = assembly
            .GetExportedTypes()
            .Where(static type =>
                type is { IsAbstract: false, IsInterface: false }
                && typeof(ConfigModule).IsAssignableFrom(type)
            )
            .ToArray();

        if (discoveredModules.Length != 1)
        {
            throw new ConfigurationException(
                $"Module assembly '{assemblyPath}' must contain exactly one public {nameof(ConfigModule)} implementation."
            );
        }

        var schema = ModuleSchemaRegistry.Get(discoveredModules[0]);

        if (!string.Equals(schema.Name, shortName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(
                $"Module assembly '{assemblyPath}' is expected to export '{shortName}', but its descriptor name is '{schema.Name}'."
            );
        }

        return new ResolvedModule(providerName, shortName, $"{providerName}.{shortName}", schema);
    }

    private static void ValidateShortName(string shortName, string owner)
    {
        if (
            string.IsNullOrWhiteSpace(shortName)
            || shortName.Contains('.')
            || shortName.AsLocalPath().FileName != shortName
        )
        {
            throw new ConfigurationException($"{owner} must have a valid short name.");
        }
    }
}
