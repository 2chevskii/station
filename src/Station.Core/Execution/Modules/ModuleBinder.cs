using System.Text.Json;

namespace Station.Core.Execution.Modules;

internal sealed class ModuleBinder
{
    public ModuleBindingResult Bind(
        ModuleSchema schema,
        ModuleContext context,
        ConfigObject rawParameters,
        bool allowDeferred
    )
    {
        var diagnostics = new List<ModuleDiagnostic>();
        var boundParameters = new List<BoundParameterRecord>();
        var rawEntries = rawParameters.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase
        );
        var usedEntries = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        var module = (ConfigModule)schema.Constructor.Invoke( [context] );

        foreach ( var parameter in schema.Parameters )
        {
            var matches = rawEntries
                .Where( entry =>
                    string.Equals( entry.Key, parameter.Name, StringComparison.OrdinalIgnoreCase )
                    || parameter.Aliases.Contains( entry.Key, StringComparer.OrdinalIgnoreCase )
                )
                .ToArray();

            if ( matches.Length > 1 )
            {
                diagnostics.Add(
                    new ModuleDiagnostic(
                        $"Parameter '{parameter.Name}' is specified more than once ({string.Join( ", ", matches.Select( static match => match.Key ) )})."
                    )
                );
                continue;
            }

            if ( matches.Length == 0 )
            {
                if ( parameter.Required )
                {
                    diagnostics.Add(
                        new ModuleDiagnostic( $"Missing required parameter '{parameter.Name}'." )
                    );
                    continue;
                }

                boundParameters.Add(
                    new BoundParameterRecord(
                        parameter.Name,
                        ModuleParameterValueConverter.FormatValue(
                            parameter.Property.GetValue( module )
                        ),
                        false,
                        true,
                        null
                    )
                );
                continue;
            }

            var match = matches[0];
            usedEntries.Add( match.Key );
            var binding = BindValue(
                parameter.Property.PropertyType,
                match.Value,
                context.Variables,
                context.TemplateEngine,
                match.Key,
                allowDeferred
            );

            if ( binding.Diagnostic is not null )
                diagnostics.Add( binding.Diagnostic );

            if ( binding.Success )
                parameter.Property.SetValue( module, binding.Value );

            boundParameters.Add(
                new BoundParameterRecord(
                    parameter.Name,
                    binding.DisplayValue,
                    binding.IsDeferred,
                    false,
                    match.Key
                )
            );
        }

        foreach ( var entry in rawEntries.Keys.Where( key => !usedEntries.Contains( key ) ) )
            diagnostics.Add( new ModuleDiagnostic( $"Unknown parameter '{entry}'." ) );

        return new ModuleBindingResult(
            diagnostics.Any( static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error )
                ? null
                : module,
            diagnostics,
            boundParameters,
            boundParameters.Any( static parameter => parameter.IsDeferred )
        );
    }

    private static (
        bool Success,
        object? Value,
        bool IsDeferred,
        string DisplayValue,
        ModuleDiagnostic? Diagnostic
        ) BindValue(
            Type targetType,
            ConfigValue rawValue,
            VariableSet variables,
            ITemplateEngine templateEngine,
            string parameterName,
            bool allowDeferred
        )
    {
        try
        {
            var resolvedValue = ConfigValueUtilities.ResolveValue(
                rawValue,
                variables,
                templateEngine,
                parameterName
            );
            var convertedValue = ModuleParameterValueConverter.ConvertValue(
                resolvedValue,
                targetType
            );
            return (
                true,
                convertedValue,
                false,
                ModuleParameterValueConverter.FormatValue( convertedValue ),
                null
            );
        }
        catch ( ConfigurationException exception )
            when ( allowDeferred
                   && exception.Message.Contains(
                       "render template",
                       StringComparison.OrdinalIgnoreCase )
                 )
        {
            return (
                false,
                null,
                true,
                "<deferred>",
                new ModuleDiagnostic(
                    $"Parameter '{parameterName}' is not fully known before execution: {exception.Message}",
                    DiagnosticSeverity.Warning
                )
            );
        }
        catch ( Exception exception )
            when ( exception
                      is ConfigurationException
                      or FormatException
                      or InvalidOperationException
                      or JsonException
                 )
        {
            return (
                false,
                null,
                false,
                "<invalid>",
                new ModuleDiagnostic(
                    $"Parameter '{parameterName}' could not be bound: {exception.Message}"
                )
            );
        }
    }
}