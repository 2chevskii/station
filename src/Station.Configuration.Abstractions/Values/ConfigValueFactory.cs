using Station.Abstractions.Configuration;

namespace Station.Configuration.Abstractions.Values;

public static class ConfigValueFactory
{
    public static ConfigValue FromClrValue( object? value, bool detectTemplateMarkup = true )
    {
        return value switch
        {
            null => ConfigNull.Instance,
            ConfigValue configValue => configValue,
            IDictionary<string, object?> mapping
                when TryCreateVariableReference( mapping, out var reference ) => reference,
            string stringValue => detectTemplateMarkup
                                  && ConfigValueUtilities.IsTemplateMarkup( stringValue )
                ? new TemplateMarkup( stringValue )
                : ConfigScalar.String( stringValue ),
            bool boolValue => ConfigScalar.Boolean( boolValue ),
            int intValue => ConfigScalar.Integer( intValue ),
            long longValue => ConfigScalar.Integer( longValue ),
            short shortValue => ConfigScalar.Integer( shortValue ),
            byte byteValue => ConfigScalar.Integer( byteValue ),
            sbyte sbyteValue => ConfigScalar.Integer( sbyteValue ),
            uint uintValue => ConfigScalar.Integer( uintValue ),
            ushort ushortValue => ConfigScalar.Integer( ushortValue ),
            float floatValue => ConfigScalar.Float( floatValue ),
            double doubleValue => ConfigScalar.Float( doubleValue ),
            decimal decimalValue => ConfigScalar.Float( (double)decimalValue ),
            IDictionary<string, object?> mapping => new ConfigObject(
                mapping.Select( pair =>
                    KeyValuePair.Create( pair.Key, FromClrValue( pair.Value, detectTemplateMarkup ) )
                )
            ),
            IDictionary<object, object?> objectMapping => new ConfigObject(
                objectMapping.Select( pair =>
                    KeyValuePair.Create(
                        Convert.ToString( pair.Key )
                        ?? throw new ConfigurationException( "Mapping keys must be strings." ),
                        FromClrValue( pair.Value, detectTemplateMarkup )
                    )
                )
            ),
            IEnumerable<object?> list when value is not string => new ConfigArray(
                list.Select( item => FromClrValue( item, detectTemplateMarkup ) )
            ),
            var _ => throw new ConfigurationException(
                $"Unsupported configuration value '{value.GetType().Name}'."
            ),
        };
    }

    private static bool TryCreateVariableReference(
        IDictionary<string, object?> mapping,
        out VariableReference reference
    )
    {
        if (
            mapping.Count == 1
            && mapping.TryGetValue( "__stationVariableReference", out var pathValue )
            && pathValue is string legacyPath
            && !string.IsNullOrWhiteSpace( legacyPath )
        )
        {
            reference = new VariableReference( legacyPath );
            return true;
        }

        if (
            mapping.TryGetValue( "$type", out var typeValue )
            && string.Equals( typeValue?.ToString(), "variable_reference", StringComparison.Ordinal )
            && mapping.TryGetValue( "name", out var nameValue )
            && nameValue is string path
            && !string.IsNullOrWhiteSpace( path )
        )
        {
            reference = new VariableReference( path );
            return true;
        }

        reference = new VariableReference( string.Empty );
        return false;
    }

    public static ConfigObject RequireObject( ConfigValue value, string sourceName )
    {
        if ( value is ConfigObject configObject )
            return configObject;

        throw new ConfigurationException(
            $"Variables from '{sourceName}' must contain a top-level mapping."
        );
    }

    public static ConfigArray RequireArray( ConfigValue value, string sourceName )
    {
        if ( value is ConfigArray configArray )
            return configArray;

        throw new ConfigurationException(
            $"Role file '{sourceName}' must contain a top-level task list."
        );
    }
}