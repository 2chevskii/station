namespace Station.Core.Plugins.Restoration;

internal sealed class PluginCacheDirectoryResolver( ITemplateEngine templateEngine )
{
    public const string VariableName = "plugin_cache_dir";

    public string? Resolve(
        string configRoot,
        VariableSet variables,
        string? commandLineDirectory
    )
    {
        var configuredDirectory = commandLineDirectory;
        if ( configuredDirectory is null && variables.Contains( VariableName ) )
            if ( !variables.TryGetValue( VariableName, out configuredDirectory ) )
                throw new ConfigurationException(
                    $"Variable '{VariableName}' must be a path string."
                );

        if ( configuredDirectory is null )
            return null;

        if ( ConfigValueUtilities.IsTemplateMarkup( configuredDirectory ) )
            configuredDirectory = templateEngine.Render(
                configuredDirectory,
                variables,
                VariableName
            );

        if ( string.IsNullOrWhiteSpace( configuredDirectory ) )
            throw new ConfigurationException(
                $"Plugin cache directory '{VariableName}' must not be empty."
            );

        try
        {
            var path = configuredDirectory.Trim();
            return Path.GetFullPath(
                Path.IsPathRooted( path ) ? path : Path.Combine( configRoot, path )
            );
        }
        catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException )
        {
            throw new ConfigurationException(
                $"Plugin cache directory '{configuredDirectory}' is invalid."
            );
        }
    }
}
