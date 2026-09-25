using System.Collections.Concurrent;
using System.Reflection;
using Station.Abstractions.Attributes;
using Station.Core.Execution.Models;

namespace Station.Core.Execution.Services;

internal static class ModuleSchemaRegistry
{
    private static readonly ConcurrentDictionary<Type, ModuleSchema> Cache = new();

    public static ModuleSchema Get(Type moduleType) => Cache.GetOrAdd(moduleType, Create);

    private static ModuleSchema Create(Type moduleType)
    {
        if (!typeof(ConfigModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ConfigurationException(
                $"Type '{moduleType.FullName}' must be a non-abstract {nameof(ConfigModule)}."
            );
        }

        var attribute =
            moduleType.GetCustomAttribute<ModuleAttribute>()
            ?? throw new ConfigurationException(
                $"Module type '{moduleType.FullName}' must declare [{nameof(ModuleAttribute)}]."
            );

        var constructor =
            moduleType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(ctor =>
                {
                    var parameters = ctor.GetParameters();
                    return parameters.Length == 1
                        && parameters[0].ParameterType == typeof(ModuleContext);
                })
            ?? throw new ConfigurationException(
                $"Module type '{moduleType.FullName}' must expose a single public constructor accepting {nameof(ModuleContext)}."
            );

        var parameters = moduleType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property =>
                (Property: property, Attribute: property.GetCustomAttribute<ParameterAttribute>())
            )
            .Where(static item => item.Attribute is not null)
            .Select(item =>
            {
                if (!item.Property.CanWrite)
                {
                    throw new ConfigurationException(
                        $"Parameter property '{moduleType.FullName}.{item.Property.Name}' must be writable."
                    );
                }

                var parameterName = string.IsNullOrWhiteSpace(item.Attribute!.Name)
                    ? ToCamelCase(item.Property.Name)
                    : item.Attribute.Name!;
                var aliases = (item.Attribute.Aliases ?? [])
                    .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (aliases.Contains(parameterName, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ConfigurationException(
                        $"Parameter '{parameterName}' on '{moduleType.FullName}' cannot repeat its canonical name as an alias."
                    );
                }

                return new ModuleParameterSchema(
                    parameterName,
                    aliases,
                    item.Attribute.Required,
                    item.Attribute.Description,
                    item.Property
                );
            })
            .ToArray();

        var allNames = parameters
            .SelectMany(parameter => new[] { parameter.Name }.Concat(parameter.Aliases))
            .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (allNames is not null)
        {
            throw new ConfigurationException(
                $"Module '{moduleType.FullName}' has duplicate parameter name or alias '{allNames.Key}'."
            );
        }

        return new ModuleSchema(
            moduleType,
            attribute.Name,
            attribute.Description,
            constructor,
            parameters
        );
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
}
