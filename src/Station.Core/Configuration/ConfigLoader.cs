using System.Globalization;
using System.Text.Json.Nodes;

using Station.Abstractions.Models;
using Station.Core.Infrastructure.Json;
using Station.Core.Infrastructure.Templates;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Station.Core.Configuration;

public sealed class ConfigLoader
{
    private const string RootManifestFileName = "station.yaml";
    private const string PluginManifestYamlFileName = "plugin-manifest.yaml";
    private const string PluginManifestYmlFileName = "plugin-manifest.yml";
    private const string RoleFileName = "role.yaml";
    private const string DefaultsFileName = "defaults.yaml";
    private const string RolesDirectoryName = "roles";
    private const string ModulesDirectoryName = "modules";
    private const string VarsDirectoryName = "vars";
    private const string PluginsDirectoryName = "plugins";
    private const string EnvironmentPrefix = "CONFIG_TOOL_VAR_";

    private static readonly string[] ArchiveExtensions = [".zip", ".tar", ".tar.gz", ".tgz", ".7z"];

    private readonly IDeserializer deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ITemplateRenderer templateRenderer;

    public ConfigLoader()
        : this(new FluidTemplateRenderer())
    {
    }

    internal ConfigLoader(ITemplateRenderer templateRenderer)
    {
        this.templateRenderer = templateRenderer;
    }

    public LoadedConfiguration Load(
        string configRoot,
        IReadOnlyDictionary<string, string>? cliVariables = null)
    {
        var fullConfigRoot = Path.GetFullPath(configRoot);
        var manifestPath = Path.Combine(fullConfigRoot, RootManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new ConfigurationException($"Missing root manifest '{manifestPath}'.");
        }

        var rootNode = LoadYamlObject(manifestPath);
        if (rootNode is not IDictionary<string, object?> manifest)
        {
            throw new ConfigurationException($"Root manifest '{manifestPath}' must contain a top-level mapping.");
        }

        var rootVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MergeVariables(rootVariables, LoadVarsDirectory(fullConfigRoot));
        MergeVariables(rootVariables, ToScalarVariables(manifest.GetOptionalMapping("vars"), manifestPath, "vars"));
        MergeVariables(rootVariables, LoadEnvironmentVariables());
        MergeVariables(rootVariables, cliVariables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var requestedRoles = manifest.GetRequiredStringList("roles", manifestPath);
        var pluginDeclarations = ParsePluginDeclarations(manifest.GetOptionalList("plugins"), manifestPath);
        var rootTasks = ParseTasks(
            manifest.GetOptionalList("tasks"),
            roleName: null,
            taskRoot: fullConfigRoot,
            roleRoot: null,
            variables: rootVariables,
            sourceName: manifestPath,
            defaultNamePrefix: "station.tasks");

        return new LoadedConfiguration(
            fullConfigRoot,
            new Dictionary<string, string>(rootVariables, StringComparer.OrdinalIgnoreCase),
            pluginDeclarations,
            requestedRoles,
            rootTasks);
    }

    public IReadOnlyList<LoadedTask> LoadRoleTasks(
        ResolvedRole role,
        IReadOnlyDictionary<string, string> rootVariables)
    {
        var roleFilePath = Path.Combine(role.RoleRoot, RoleFileName);
        if (!File.Exists(roleFilePath))
        {
            throw new ConfigurationException($"Role '{role.FullName}' is missing '{RoleFileName}'.");
        }

        var roleVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MergeVariables(roleVariables, LoadOptionalDefaults(role.RoleRoot));
        MergeVariables(roleVariables, rootVariables);

        var taskNodes = LoadYamlObject(roleFilePath) as IEnumerable<object?>;
        if (taskNodes is null)
        {
            throw new ConfigurationException($"Role file '{roleFilePath}' must contain a top-level task list.");
        }

        return ParseTasks(
            taskNodes.ToList(),
            role.ShortName,
            role.RoleRoot,
            role.RoleRoot,
            roleVariables,
            roleFilePath,
            $"{role.ShortName}.tasks");
    }

    public ResolvedPluginManifest LoadPluginManifest(string pluginRoot)
    {
        var fullPluginRoot = Path.GetFullPath(pluginRoot);
        var yamlPath = Path.Combine(fullPluginRoot, PluginManifestYamlFileName);
        var ymlPath = Path.Combine(fullPluginRoot, PluginManifestYmlFileName);

        if (File.Exists(yamlPath) && File.Exists(ymlPath))
        {
            throw new ConfigurationException(
                $"Plugin root '{fullPluginRoot}' contains both '{PluginManifestYamlFileName}' and '{PluginManifestYmlFileName}'.");
        }

        var manifestPath = File.Exists(yamlPath)
            ? yamlPath
            : File.Exists(ymlPath)
                ? ymlPath
                : throw new ConfigurationException(
                    $"Plugin root '{fullPluginRoot}' is missing '{PluginManifestYamlFileName}' or '{PluginManifestYmlFileName}'.");

        var manifest = LoadYaml<PluginManifestFile>(manifestPath);
        var name = manifest.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ConfigurationException($"Plugin manifest '{manifestPath}' must declare a non-empty 'name'.");
        }

        var version = manifest.Version?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ConfigurationException($"Plugin manifest '{manifestPath}' must declare a non-empty 'version'.");
        }

        var roles = NormalizeExportList(manifest.Roles, "roles", manifestPath);
        var modules = NormalizeExportList(manifest.Modules, "modules", manifestPath);

        foreach (var role in roles)
        {
            var roleFilePath = Path.Combine(fullPluginRoot, RolesDirectoryName, role, RoleFileName);
            if (!File.Exists(roleFilePath))
            {
                throw new ConfigurationException(
                    $"Plugin '{name}' exports role '{role}', but '{roleFilePath}' does not exist.");
            }
        }

        foreach (var module in modules)
        {
            var modulePath = Path.Combine(fullPluginRoot, ModulesDirectoryName, module, $"{module}.dll");
            if (!File.Exists(modulePath))
            {
                throw new ConfigurationException(
                    $"Plugin '{name}' exports module '{module}', but '{modulePath}' does not exist.");
            }
        }

        return new ResolvedPluginManifest(name, version, fullPluginRoot, roles, modules);
    }

    public IReadOnlyList<ResolvedRole> DiscoverLocalRoles(string configRoot)
    {
        var rolesRoot = Path.Combine(Path.GetFullPath(configRoot), RolesDirectoryName);
        if (!Directory.Exists(rolesRoot))
        {
            return [];
        }

        return Directory.GetDirectories(rolesRoot)
            .Select(roleRoot => new ResolvedRole(
                "station.local",
                Path.GetFileName(roleRoot),
                $"station.local.{Path.GetFileName(roleRoot)}",
                roleRoot))
            .ToArray();
    }

    public IReadOnlyList<string> DiscoverLocalModulePaths(string configRoot)
    {
        var modulesRoot = Path.Combine(Path.GetFullPath(configRoot), ModulesDirectoryName);
        if (!Directory.Exists(modulesRoot))
        {
            return [];
        }

        return Directory.GetDirectories(modulesRoot)
            .Select(moduleRoot => Path.Combine(moduleRoot, $"{Path.GetFileName(moduleRoot)}.dll"))
            .Where(File.Exists)
            .ToArray();
    }

    public string GetPluginsDirectory(string configRoot) =>
        Path.Combine(Path.GetFullPath(configRoot), PluginsDirectoryName);

    private List<LoadedTask> ParseTasks(
        IReadOnlyList<object?>? taskNodes,
        string? roleName,
        string taskRoot,
        string? roleRoot,
        IReadOnlyDictionary<string, string> variables,
        string sourceName,
        string defaultNamePrefix)
    {
        var tasks = new List<LoadedTask>();
        if (taskNodes is null)
        {
            return tasks;
        }

        for (var index = 0; index < taskNodes.Count; index++)
        {
            if (taskNodes[index] is not IDictionary<string, object?> taskMapping)
            {
                throw new ConfigurationException(
                    $"Task #{index + 1} in '{sourceName}' must be a mapping.");
            }

            var renderedTask = RenderTask(taskMapping, variables, sourceName, defaultNamePrefix, index + 1);
            tasks.Add(new LoadedTask(
                renderedTask.Name,
                renderedTask.ModuleName,
                renderedTask.Parameters,
                taskRoot,
                roleName,
                roleRoot,
                new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase)));
        }

        return tasks;
    }

    private (string Name, string ModuleName, JsonObject Parameters) RenderTask(
        IDictionary<string, object?> taskMapping,
        IReadOnlyDictionary<string, string> variables,
        string sourceName,
        string defaultNamePrefix,
        int oneBasedIndex)
    {
        string? explicitName = null;
        KeyValuePair<string, object?>? moduleEntry = null;

        foreach (var (key, value) in taskMapping)
        {
            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
            {
                explicitName = value?.ToString();
                continue;
            }

            if (moduleEntry is not null)
            {
                throw new ConfigurationException(
                    $"Task in '{sourceName}' must declare exactly one module key besides 'name'.");
            }

            moduleEntry = KeyValuePair.Create(key, value);
        }

        if (moduleEntry is null)
        {
            throw new ConfigurationException($"Task in '{sourceName}' does not declare a module.");
        }

        var moduleName = moduleEntry.Value.Key.Trim();
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new ConfigurationException($"Task in '{sourceName}' declares an empty module name.");
        }

        if (moduleEntry.Value.Value is not IDictionary<string, object?> rawParameters)
        {
            throw new ConfigurationException(
                $"Task module '{moduleName}' in '{sourceName}' must map to an object for now.");
        }

        var renderedParameters = JsonNodeHelpers.RenderStringScalars(
            JsonNodeHelpers.ToJsonObject(rawParameters),
            variables,
            templateRenderer,
            sourceName);

        var taskNameTemplate = string.IsNullOrWhiteSpace(explicitName)
            ? $"{defaultNamePrefix}.{oneBasedIndex}"
            : explicitName;
        var taskName = templateRenderer.Render(taskNameTemplate, variables, sourceName);

        return (taskName, moduleName, renderedParameters);
    }

    private IReadOnlyList<PluginDeclaration> ParsePluginDeclarations(IReadOnlyList<object?>? nodes, string sourceName)
    {
        var declarations = new List<PluginDeclaration>();
        foreach (var node in nodes ?? [])
        {
            declarations.Add(node switch
            {
                string shortForm => ParsePluginShortForm(shortForm),
                IDictionary<string, object?> mapping => ParsePluginLongForm(mapping, sourceName),
                _ => throw new ConfigurationException(
                    $"Plugins in '{sourceName}' must be declared as strings or mappings."),
            });
        }

        return declarations;
    }

    private PluginDeclaration ParsePluginShortForm(string shortForm)
    {
        var trimmed = shortForm.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ConfigurationException("Plugin declarations must not be empty.");
        }

        if (trimmed.StartsWith("nuget://", StringComparison.OrdinalIgnoreCase))
        {
            var idAndVersion = trimmed["nuget://".Length..];
            var (id, version) = SplitPackageCoordinate(idAndVersion);
            return new PluginDeclaration(PluginProviderKind.NuGet, trimmed, Id: id, Version: version);
        }

        if (trimmed.StartsWith("npm://", StringComparison.OrdinalIgnoreCase))
        {
            var idAndVersion = trimmed["npm://".Length..];
            var (id, version) = SplitPackageCoordinate(idAndVersion);
            return new PluginDeclaration(PluginProviderKind.Npm, trimmed, Id: id, Version: version);
        }

        if (trimmed.StartsWith("git://", StringComparison.OrdinalIgnoreCase))
        {
            var (url, gitRef) = SplitGitReference(trimmed);
            return new PluginDeclaration(PluginProviderKind.Git, trimmed, Url: url, Ref: gitRef);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            var normalized = absoluteUri.ToString();
            if (HasArchiveExtension(absoluteUri.AbsolutePath))
            {
                return new PluginDeclaration(PluginProviderKind.Url, trimmed, Url: normalized);
            }

            if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase) || UrlLooksLikeGitRepository(absoluteUri))
            {
                var (url, gitRef) = SplitGitReference(normalized);
                return new PluginDeclaration(PluginProviderKind.Git, trimmed, Url: url, Ref: gitRef);
            }

            throw new ConfigurationException(
                $"Remote plugin source '{trimmed}' requires either an archive extension or an explicit provider.");
        }

        return new PluginDeclaration(PluginProviderKind.Local, trimmed, Name: trimmed);
    }

    private static PluginDeclaration ParsePluginLongForm(IDictionary<string, object?> mapping, string sourceName)
    {
        var providerName = mapping.TryGetValue("provider", out var providerValue)
            ? providerValue?.ToString()
            : null;

        var provider = string.IsNullOrWhiteSpace(providerName)
            ? InferLongFormProvider(mapping)
            : providerName.Trim().ToLowerInvariant() switch
            {
                "local" => PluginProviderKind.Local,
                "git" => PluginProviderKind.Git,
                "url" => PluginProviderKind.Url,
                "nuget" => PluginProviderKind.NuGet,
                "npm" => PluginProviderKind.Npm,
                _ => throw new ConfigurationException(
                    $"Unsupported plugin provider '{providerName}' in '{sourceName}'."),
            };

        return provider switch
        {
            PluginProviderKind.Local => new PluginDeclaration(
                provider,
                mapping.RequireString("name", sourceName),
                Name: mapping.RequireString("name", sourceName)),
            PluginProviderKind.Git => new PluginDeclaration(
                provider,
                mapping.RequireString("url", sourceName),
                Url: mapping.RequireString("url", sourceName),
                Ref: mapping.GetOptionalString("ref")),
            PluginProviderKind.Url => new PluginDeclaration(
                provider,
                mapping.RequireString("url", sourceName),
                Url: mapping.RequireString("url", sourceName)),
            PluginProviderKind.NuGet => new PluginDeclaration(
                provider,
                mapping.RequireString("id", sourceName),
                Id: mapping.RequireString("id", sourceName),
                Version: mapping.GetOptionalString("version"),
                Feed: mapping.GetOptionalString("feed")),
            PluginProviderKind.Npm => new PluginDeclaration(
                provider,
                mapping.RequireString("id", sourceName),
                Id: mapping.RequireString("id", sourceName),
                Version: mapping.GetOptionalString("version"),
                Registry: mapping.GetOptionalString("registry")),
            _ => throw new ConfigurationException($"Unsupported plugin provider '{provider}'."),
        };
    }

    private static PluginProviderKind InferLongFormProvider(IDictionary<string, object?> mapping)
    {
        if (mapping.ContainsKey("name"))
        {
            return PluginProviderKind.Local;
        }

        throw new ConfigurationException("Plugin object declarations without 'provider' currently require a 'name'.");
    }

    private Dictionary<string, string> LoadVarsDirectory(string configRoot)
    {
        var varsRoot = Path.Combine(configRoot, VarsDirectoryName);
        if (!Directory.Exists(varsRoot))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(varsRoot, "*.yaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            MergeVariables(variables, ToScalarVariables(LoadYamlObject(file), file));
        }

        return variables;
    }

    private Dictionary<string, string> LoadOptionalDefaults(string roleRoot)
    {
        var defaultsPath = Path.Combine(roleRoot, DefaultsFileName);
        return File.Exists(defaultsPath)
            ? ToScalarVariables(LoadYamlObject(defaultsPath), defaultsPath)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> LoadEnvironmentVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<object>())
        {
            var keyString = key.ToString();
            if (keyString is null || !keyString.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            variables[keyString[EnvironmentPrefix.Length..]] =
                Environment.GetEnvironmentVariable(keyString) ?? string.Empty;
        }

        return variables;
    }

    private object? LoadYamlObject(string path)
    {
        using var reader = File.OpenText(path);
        return JsonNodeHelpers.NormalizeYamlValue(deserializer.Deserialize<object?>(reader));
    }

    private T LoadYaml<T>(string path)
    {
        using var reader = File.OpenText(path);
        return deserializer.Deserialize<T>(reader);
    }

    private static IReadOnlyList<string> NormalizeExportList(IEnumerable<string>? values, string propertyName, string sourceName)
    {
        var exports = (values ?? [])
            .Select(value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (exports.Length == 0)
        {
            return [];
        }

        foreach (var export in exports)
        {
            if (export.Contains('.') || export.Contains(Path.DirectorySeparatorChar) || export.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ConfigurationException(
                    $"Plugin manifest '{sourceName}' contains invalid {propertyName} export '{export}'. Exports must be short names.");
            }
        }

        return exports;
    }

    private static (string Id, string? Version) SplitPackageCoordinate(string value)
    {
        var separatorIndex = value.LastIndexOf('@');
        if (separatorIndex <= 0)
        {
            return (value, null);
        }

        if (separatorIndex == 0 && value.StartsWith('@'))
        {
            return (value, null);
        }

        if (value.StartsWith('@') && value.Count(static character => character == '@') == 1)
        {
            return (value, null);
        }

        var id = value[..separatorIndex];
        var version = value[(separatorIndex + 1)..];
        return (id, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    private static (string Url, string? Ref) SplitGitReference(string value)
    {
        var lastAt = value.LastIndexOf('@');
        if (lastAt <= 0)
        {
            return (value, null);
        }

        var lastSlash = value.LastIndexOf('/');
        var lastColon = value.LastIndexOf(':');
        var markerIndex = Math.Max(lastSlash, lastColon);
        if (lastAt <= markerIndex)
        {
            return (value, null);
        }

        var url = value[..lastAt];
        var gitRef = value[(lastAt + 1)..];
        return string.IsNullOrWhiteSpace(gitRef) ? (value, null) : (url, gitRef);
    }

    private static bool UrlLooksLikeGitRepository(Uri uri)
    {
        var lastSegment = uri.Segments.LastOrDefault()?.TrimEnd('/');
        return !string.IsNullOrWhiteSpace(lastSegment) && !Path.HasExtension(lastSegment);
    }

    private static bool HasArchiveExtension(string value) =>
        ArchiveExtensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static void MergeVariables(IDictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
        {
            target[key] = value;
        }
    }

    private static Dictionary<string, string> ToScalarVariables(object? value, string sourceName, string? propertyName = null)
    {
        if (value is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (value is not IDictionary<string, object?> variables)
        {
            var name = propertyName is null ? sourceName : $"{sourceName}:{propertyName}";
            throw new ConfigurationException($"Variables from '{name}' must contain a top-level mapping.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rawValue) in variables)
        {
            result[key] = rawValue switch
            {
                null => string.Empty,
                string stringValue => stringValue,
                bool boolValue => boolValue ? "true" : "false",
                int intValue => intValue.ToString(CultureInfo.InvariantCulture),
                long longValue => longValue.ToString(CultureInfo.InvariantCulture),
                double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
                decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
                _ => throw new ConfigurationException(
                    $"Variables from '{sourceName}' must be scalar values. '{key}' is not scalar."),
            };
        }

        return result;
    }
}

internal static class ConfigLoaderValueHelpers
{
    public static IReadOnlyList<string> GetRequiredStringList(
        this IDictionary<string, object?> mapping,
        string propertyName,
        string sourceName)
    {
        var values = mapping.GetOptionalList(propertyName);
        if (values is null || values.Count == 0)
        {
            throw new ConfigurationException($"'{sourceName}' must declare a non-empty '{propertyName}' list.");
        }

        return values.Select(value => value?.ToString()?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    public static IReadOnlyList<object?>? GetOptionalList(this IDictionary<string, object?> mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value as IReadOnlyList<object?> ??
               (value as IEnumerable<object?>)?.ToArray() ??
               throw new ConfigurationException($"Property '{propertyName}' must be a list.");
    }

    public static IDictionary<string, object?>? GetOptionalMapping(this IDictionary<string, object?> mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value as IDictionary<string, object?> ??
               throw new ConfigurationException($"Property '{propertyName}' must be a mapping.");
    }

    public static string RequireString(this IDictionary<string, object?> mapping, string propertyName, string sourceName)
    {
        if (!mapping.TryGetValue(propertyName, out var value) || string.IsNullOrWhiteSpace(value?.ToString()))
        {
            throw new ConfigurationException($"Property '{propertyName}' is required in '{sourceName}'.");
        }

        return value!.ToString()!;
    }

    public static string? GetOptionalString(this IDictionary<string, object?> mapping, string propertyName)
    {
        return mapping.TryGetValue(propertyName, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value!.ToString()
            : null;
    }
}
