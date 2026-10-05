using Station.Abstractions.Configuration;
using Station.Abstractions.FileSystem;

using TruePath;

namespace Station.Modules.Builtin.Dotfile;

internal static class HostFilePaths
{
    public static AbsolutePath Resolve( string path ) =>
        Environment.ExpandEnvironmentVariables( path ).AsLocalPath().ResolveToCurrentDirectory();

    public static AbsolutePath ResolveDestination( string path )
    {
        if ( string.IsNullOrWhiteSpace( path ) )
            throw new ConfigurationException( "Parameter 'destination' must be a non-empty file path." );

        return Resolve( path );
    }

    public static AbsolutePath ResolveTarget( AbsolutePath destination )
    {
        var info = new FileInfo( destination.Value );
        var target = info.LinkTarget is not null
            ? info.ResolveLinkTarget( returnFinalTarget: true )!.FullName.AsAbsolutePath()
            : destination;
        if ( Directory.Exists( target.Value ) )
            throw new ConfigurationException( $"The destination '{destination}' is a directory." );

        return target;
    }

    public static bool RequiresAdmin( string destination )
    {
        var resolved = ResolveTarget( ResolveDestination( destination ) );
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
