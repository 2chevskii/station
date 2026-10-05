using System.CommandLine;
using System.CommandLine.Parsing;

using Station.Abstractions.FileSystem;

using TruePath;

namespace Station.Cli.Commands;

internal sealed class CommandLineOptions
{
    private readonly Option<string> config = new Option<string>( "--config" )
    {
        Description = "Path to the config directory.",
        DefaultValueFactory = _ => AbsolutePath.CurrentWorkingDirectory.Value,
        Arity = ArgumentArity.ExactlyOne,
        Recursive = true,
    };

    private readonly Option<string[]> variables = new Option<string[]>( "--set" )
    {
        Description = "Override a variable with key=value.",
        AllowMultipleArgumentsPerToken = true,
        Arity = ArgumentArity.OneOrMore,
        Recursive = true,
    };

    private readonly Option<string?> pluginCacheDirectory = new Option<string?>( "--plugin-cache-dir" )
    {
        Description = "Override the plugin cache directory.",
        Arity = ArgumentArity.ExactlyOne,
        Recursive = true,
    };

    private readonly Option<string> output = new Option<string>( "--output" )
    {
        Description = "Result format: text or json.",
        DefaultValueFactory = _ => "text",
        Arity = ArgumentArity.ExactlyOne,
        Recursive = true,
    };

    public CommandLineOptions()
    {
        output.AcceptOnlyFromAmong( StringComparer.OrdinalIgnoreCase, "text", "json" );
        config.Validators.Add( ValidateSingleOccurrence );
        pluginCacheDirectory.Validators.Add( ValidateSingleOccurrence );
        output.Validators.Add( ValidateSingleOccurrence );
        variables.Validators.Add( result =>
        {
            foreach ( var token in result.Tokens )
            {
                var separator = token.Value.IndexOf( '=' );
                if ( separator <= 0 || string.IsNullOrWhiteSpace( token.Value[..separator] ) )
                    result.AddError(
                        $"Invalid '--set' value '{token.Value}'. Expected a non-empty name followed by '=value'."
                    );
            }
        } );
    }

    private static void ValidateSingleOccurrence( OptionResult result )
    {
        if ( result.IdentifierTokenCount > 1 )
            result.AddError( $"Option '{result.Option.Name}' can only be specified once." );
    }

    public void AddTo( Command command )
    {
        command.Options.Add( config );
        command.Options.Add( variables );
        command.Options.Add( pluginCacheDirectory );
        command.Options.Add( output );
    }

    public bool UseJson( ParseResult parseResult )
    {
        var tokens = parseResult.GetResult( output )?.Tokens;
        return tokens is { Count: > 0 }
            && string.Equals( tokens[^1].Value, "json", StringComparison.OrdinalIgnoreCase );
    }

    public string? GetMissingVariableValueError( ParseResult parseResult )
    {
        if ( parseResult.GetResult( variables ) is not { IdentifierTokenCount: > 1 } )
            return null;

        var tokens = parseResult.Tokens;
        for ( var index = 0; index < tokens.Count; index++ )
        {
            if ( tokens[index].Type != TokenType.Option || tokens[index].Value != variables.Name )
                continue;

            if ( index + 1 == tokens.Count || tokens[index + 1].Type != TokenType.Argument )
                return "Option '--set' requires a key=value argument for each occurrence.";
        }

        return null;
    }

    public CommandInvocation Read( ParseResult parseResult )
    {
        var overrides = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
        foreach ( var entry in parseResult.GetValue( variables ) ?? [] )
        {
            var separator = entry.IndexOf( '=' );
            overrides[entry[..separator].Trim()] = entry[(separator + 1)..];
        }

        return new CommandInvocation(
            parseResult.GetValue( config )!.AsLocalPath().ResolveToCurrentDirectory().Value,
            overrides,
            parseResult.GetValue( pluginCacheDirectory )
        );
    }
}
