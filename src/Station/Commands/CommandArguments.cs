namespace Station.Commands;

// Preserve global option placement and multi-value --set syntax when invoking Spectre's parser.
internal sealed class CommandArguments
{
    public IReadOnlyList<string> Values { get; }
    public string CommandName { get; }
    public bool UseJson { get; }
    public bool ShowHelp { get; }
    public string? Error { get; }

    public CommandArguments( string[] arguments )
    {
        var command = new List<string>();
        var options = new List<string>();
        var scalarOptions = new HashSet<string>( StringComparer.Ordinal );
        string? output = null;

        for ( var index = 0; index < arguments.Length; index++ )
        {
            var argument = arguments[index];
            var optionName = argument.Split( '=', 2 )[0];
            if ( optionName is "--config" or "--plugin-cache-dir" or "--output"
                && !scalarOptions.Add( optionName ) )
            {
                Error ??= $"Option '{optionName}' can only be specified once.";
            }

            if ( argument is "--config" or "--plugin-cache-dir" or "--output" or "--set" )
            {
                options.Add( argument );
                if ( index + 1 < arguments.Length && !IsOption( arguments[index + 1] ) )
                {
                    var value = arguments[++index];
                    options.Add( value );
                    if ( argument == "--output" )
                        output = value;

                    if ( argument == "--set" )
                    {
                        while ( index + 1 < arguments.Length
                            && !IsOption( arguments[index + 1] )
                            && arguments[index + 1].Contains( '=' ) )
                        {
                            options.Add( "--set" );
                            options.Add( arguments[++index] );
                        }
                    }
                }
                else
                {
                    Error ??= $"Option '{argument}' requires a value for each occurrence.";
                }

                continue;
            }

            if ( argument.StartsWith( "--output=", StringComparison.Ordinal ) )
                output = argument["--output=".Length..];

            if ( command.Count == 0
                && argument is "restore" or "explain" or "list" or "validate" or "check" or "apply" )
            {
                command.Add( argument );
            }
            else if ( command is ["list"] && argument is "roles" or "modules" )
            {
                command.Add( argument );
            }
            else
            {
                options.Add( argument );
            }
        }

        CommandName = command.Count == 0 ? "station" : string.Join( ' ', command );
        UseJson = string.Equals( output, "json", StringComparison.OrdinalIgnoreCase );
        ShowHelp = options.Any( argument => argument is "--help" or "-h" or "-?" or "--version" );
        if ( options.Contains( "--version" ) )
        {
            options.Remove( "--version" );
            options.Insert( 0, "--version" );
        }

        Values = arguments.Length == 0 ? ["--help"] : [..command, ..options];
    }

    private static bool IsOption( string argument ) => argument.StartsWith( '-' );
}
