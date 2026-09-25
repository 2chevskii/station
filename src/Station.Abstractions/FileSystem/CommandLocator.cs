using TruePath.SystemIo;

namespace Station.Abstractions.FileSystem;

public static class CommandLocator
{
    private const string PathVariableName = "PATH";
    private const string ExecutableExtensionsVariableName = "PATHEXT";
    private const string DefaultWindowsExecutableExtensions = ".EXE;.CMD;.BAT;.COM;.PS1";

    public static bool Exists( string commandName )
    {
        if ( string.IsNullOrWhiteSpace( commandName ) )
            return false;

        if ( commandName.AsLocalPath().IsAbsolute )
            return commandName.AsLocalPath().ResolveToCurrentDirectory().ExistsFile();

        var path = Environment.GetEnvironmentVariable( PathVariableName ) ?? string.Empty;
        var pathEntries = path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        var executableExtensions = Environment.GetEnvironmentVariable(
            ExecutableExtensionsVariableName
        );
        var extensions = OperatingSystem.IsWindows()
            ? (executableExtensions ?? DefaultWindowsExecutableExtensions).Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            : [string.Empty];

        foreach ( var directory in pathEntries )
        {
            var directPath = (directory.AsLocalPath() / commandName).ResolveToCurrentDirectory();
            if ( directPath.ExistsFile() )
                return true;

            foreach ( var extension in extensions )
            {
                var candidate = (
                    directory.AsLocalPath() / (commandName + extension)
                ).ResolveToCurrentDirectory();
                if ( candidate.ExistsFile() )
                    return true;
            }
        }

        return false;
    }
}