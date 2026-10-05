namespace Station.Abstractions.FileSystem;

public static class CommandLocator
{
    private const string PathVariableName = "PATH";
    private const string ExecutableExtensionsVariableName = "PATHEXT";
    private const string DefaultWindowsExecutableExtensions = ".COM;.EXE;.BAT;.CMD";

    public static bool Exists( string commandName ) => Find( commandName ) is not null;

    public static string? Find(
        string commandName,
        string? workingDirectory = null,
        string? searchPath = null,
        string? executableExtensions = null
    )
    {
        if ( string.IsNullOrWhiteSpace( commandName ) )
            return null;

        var root = Path.GetFullPath( workingDirectory ?? Environment.CurrentDirectory );
        var extensions = (
            executableExtensions
            ?? Environment.GetEnvironmentVariable( ExecutableExtensionsVariableName )
            ?? DefaultWindowsExecutableExtensions
        ).Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

        if ( Path.IsPathRooted( commandName ) || commandName.Contains( Path.DirectorySeparatorChar )
            || commandName.Contains( Path.AltDirectorySeparatorChar ) )
            return FindInDirectory( Path.GetFullPath( commandName, root ), extensions );

        var path = searchPath ?? Environment.GetEnvironmentVariable( PathVariableName ) ?? string.Empty;
        var pathEntries = path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        foreach ( var directory in pathEntries )
        {
            var candidate = Path.GetFullPath(
                Path.Combine( directory.Trim( '"' ), commandName ),
                root
            );
            var executable = FindInDirectory( candidate, extensions );
            if ( executable is not null )
                return executable;
        }

        return null;
    }

    private static string? FindInDirectory( string candidate, IReadOnlyList<string> extensions )
    {
        if ( !OperatingSystem.IsWindows() )
        {
            if ( !File.Exists( candidate ) )
                return null;

            var permissions = File.GetUnixFileMode( candidate );
            var executablePermissions = UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (permissions & executablePermissions) != 0 ? candidate : null;
        }

        if ( IsWindowsExecutableExtension( Path.GetExtension( candidate ) )
            && File.Exists( candidate ) )
            return candidate;

        foreach ( var extension in extensions.Where( IsWindowsExecutableExtension ) )
        {
            var executable = candidate + extension;
            if ( File.Exists( executable ) )
                return executable;
        }

        return null;
    }

    private static bool IsWindowsExecutableExtension( string extension ) =>
        extension.ToUpperInvariant() is ".EXE" or ".COM" or ".CMD" or ".BAT";
}
