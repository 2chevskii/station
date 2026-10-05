namespace Station.Core.Configuration.Loading;

internal static class VariableOverrideLoader
{
    private const string EnvironmentPrefix = "CONFIG_TOOL_VAR_";

    public static ConfigObject LoadGlobals()
    {
        var globals = new ConfigObject(
            new Dictionary<string, ConfigValue>
            {
                {
                    "user_home",
                    new ConfigScalar(
                        ConfigScalarKind.String,
                        Environment.GetFolderPath( Environment.SpecialFolder.UserProfile )
                    )
                },
            }
        );
        return globals;
    }

    public static ConfigObject LoadEnvironment()
    {
        var variables = new ConfigObject();
        foreach ( var key in Environment.GetEnvironmentVariables().Keys.Cast<object>() )
        {
            var keyString = key.ToString();
            if (
                keyString is null
                || !keyString.StartsWith( EnvironmentPrefix, StringComparison.OrdinalIgnoreCase )
            )
                continue;

            variables = SetVariablePath(
                variables,
                keyString[EnvironmentPrefix.Length..],
                ConfigScalar.String( Environment.GetEnvironmentVariable( keyString ) ?? string.Empty )
            );
        }

        return variables;
    }

    public static ConfigObject LoadCli( IReadOnlyDictionary<string, string>? cliVariables )
    {
        var variables = new ConfigObject();
        foreach ( var (key, value) in cliVariables ?? new Dictionary<string, string>() )
            variables = SetVariablePath( variables, key, ConfigScalar.String( value ) );

        return variables;
    }

    private static ConfigObject SetVariablePath(
        ConfigObject variables,
        string path,
        ConfigValue value
    )
    {
        var segments = ConfigValueUtilities.SplitPath( path ).ToArray();
        if ( segments.Length == 0 )
            throw new ConfigurationException( "Variable names must not be empty." );

        return SetVariablePath( variables, segments, 0, value );
    }

    private static ConfigObject SetVariablePath(
        ConfigObject current,
        IReadOnlyList<string> segments,
        int index,
        ConfigValue value
    )
    {
        var updated = current.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase
        );

        if ( index == segments.Count - 1 )
        {
            updated[segments[index]] = value;
            return new ConfigObject( updated );
        }

        var segment = segments[index];
        var child =
            updated.TryGetValue( segment, out var existing ) && existing is ConfigObject childObject
                ? childObject
                : new ConfigObject();
        updated[segment] = SetVariablePath( child, segments, index + 1, value );
        return new ConfigObject( updated );
    }
}
