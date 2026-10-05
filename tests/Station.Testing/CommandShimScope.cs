using System.Text;

using Station.Abstractions.FileSystem;

using TruePath.SystemIo;

namespace Station.Testing;

public sealed class CommandShimScope : IDisposable
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding( false );
    private readonly string? originalPath;
    private readonly string? originalPathExt;
    private readonly string? powerShellPath;
    private readonly TemporaryDirectory directory = new TemporaryDirectory();

    public CommandShimScope( bool includeHostPath = true )
    {
        originalPath = Environment.GetEnvironmentVariable( "PATH" );
        originalPathExt = Environment.GetEnvironmentVariable( "PATHEXT" );
        powerShellPath = CommandLocator.Find( "pwsh" );
        var dotnetPath = CommandLocator.Find( "dotnet" );
        if ( !includeHostPath && dotnetPath is not null )
        {
            var command = OperatingSystem.IsWindows()
                ? $"\"{dotnetPath.Replace( "%", "%%" )}\" %*"
                : $"exec {QuoteForShell( dotnetPath )} \"$@\"";
            AddCommand( "dotnet", command );
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            includeHostPath
                ? directory.Path + System.IO.Path.PathSeparator + originalPath
                : directory.Path
        );
        if ( OperatingSystem.IsWindows() )
            Environment.SetEnvironmentVariable( "PATHEXT", ".CMD;.BAT;.EXE;.COM;.PS1" );
    }

    public string Path => directory.Path;

    public string AddCommand( string name, string body )
    {
        ValidateName( name );
        var path = directory.GetPath( OperatingSystem.IsWindows() ? $"{name}.cmd" : name );
        var content = OperatingSystem.IsWindows()
            ? "@echo off\r\n" + body.Trim() + "\r\n"
            : "#!/bin/sh\nset -eu\n" + body.Trim() + "\n";
        path.AsAbsolutePath().WriteAllText( content, Utf8WithoutBom );
        if ( !OperatingSystem.IsWindows() )
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

        return path;
    }

    public string AddPowerShellCommand( string name, string body )
    {
        ValidateName( name );
        if ( powerShellPath is null )
            throw new InvalidOperationException( "PowerShell 7 (pwsh) is required by command fixtures." );

        var scriptPath = directory.GetPath( $"{name}.ps1" );
        var script = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n"
            + "$OutputEncoding = [Console]::OutputEncoding\n"
            + body.Replace( "\r\n", "\n" );
        scriptPath.AsAbsolutePath().WriteAllText( script, Utf8WithoutBom );
        var command = OperatingSystem.IsWindows()
            ? $"\"{powerShellPath.Replace( "%", "%%" )}\" -NoLogo -NoProfile -File \"%~dp0{name}.ps1\" %*"
            : $"exec {QuoteForShell( powerShellPath )} -NoLogo -NoProfile -File {QuoteForShell( scriptPath )} \"$@\"";
        return AddCommand( name, command );
    }

    private static string QuoteForShell( string value ) =>
        "'" + value.Replace( "'", "'\"'\"'", StringComparison.Ordinal ) + "'";

    private static void ValidateName( string name )
    {
        if ( string.IsNullOrWhiteSpace( name ) || name is "." or ".."
            || name.Any( character => !char.IsAsciiLetterOrDigit( character )
                && character is not ('-' or '_' or '.') ) )
            throw new ArgumentException( "A fixture command needs a simple file name.", nameof( name ) );
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable( "PATH", originalPath );
        Environment.SetEnvironmentVariable( "PATHEXT", originalPathExt );
        directory.Dispose();
    }
}
