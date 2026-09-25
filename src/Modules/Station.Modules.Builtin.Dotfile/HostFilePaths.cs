using Station.Abstractions.FileSystem;

using TruePath;

namespace Station.Modules.Builtin.Dotfile;

internal static class HostFilePaths
{
    public static AbsolutePath Resolve( string path ) =>
        Environment.ExpandEnvironmentVariables( path ).AsLocalPath().ResolveToCurrentDirectory();

    public static bool RequiresAdmin( string destination )
    {
        var resolved = Resolve( destination );
        var protectedRoots = new[]
        {
            Environment.GetFolderPath( Environment.SpecialFolder.Windows ),
            Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles ),
            Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 ),
            Environment.GetFolderPath( Environment.SpecialFolder.CommonApplicationData ),
        }.Where( static path => !string.IsNullOrWhiteSpace( path ) );

        return protectedRoots.Any( root => IsWithinRoot( resolved, Resolve( root ) ) );
    }

    private static bool IsWithinRoot( AbsolutePath path, AbsolutePath root )
    {
        for ( AbsolutePath? current = path; current is { } candidate; current = candidate.Parent )
            if ( candidate.Equals( root, AbsolutePath.PlatformDefaultComparer ) )
                return true;

        return false;
    }
}