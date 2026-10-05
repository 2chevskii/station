using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

using Station.Abstractions.FileSystem;

using TruePath;

namespace Station.Commands;

internal sealed class StationSettings : CommandSettings
{
    [CommandOption( "--config <PATH>" )]
    [Description( "Path to the config directory." )]
    public string ConfigRoot { get; set; } = AbsolutePath.CurrentWorkingDirectory.Value;

    [CommandOption( "--set <KEY=VALUE>" )]
    [Description( "Override a variable with key=value. Can be repeated." )]
    public string[] Variables { get; set; } = [];

    [CommandOption( "--plugin-cache-dir <PATH>" )]
    [Description( "Override the plugin cache directory." )]
    public string? PluginCacheDirectory { get; set; }

    [CommandOption( "--output <FORMAT>" )]
    [Description( "Result format: text or json." )]
    [DefaultValue( "text" )]
    public string Output { get; set; } = "text";

    public bool UseJson => string.Equals( Output, "json", StringComparison.OrdinalIgnoreCase );

    public override ValidationResult Validate()
    {
        if ( !UseJson && !string.Equals( Output, "text", StringComparison.OrdinalIgnoreCase ) )
            return ValidationResult.Error( $"Invalid output format '{Output}'. Expected text or json." );

        foreach ( var entry in Variables )
        {
            var separator = entry.IndexOf( '=' );
            if ( separator <= 0 || string.IsNullOrWhiteSpace( entry[..separator] ) )
            {
                return ValidationResult.Error(
                    $"Invalid '--set' value '{entry}'. Expected a non-empty name followed by '=value'."
                );
            }
        }

        return ValidationResult.Success();
    }

    public CommandInvocation ToInvocation()
    {
        var overrides = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
        foreach ( var entry in Variables )
        {
            var separator = entry.IndexOf( '=' );
            overrides[entry[..separator].Trim()] = entry[(separator + 1)..];
        }

        return new CommandInvocation(
            ConfigRoot.AsLocalPath().ResolveToCurrentDirectory().Value,
            overrides,
            PluginCacheDirectory
        );
    }
}
