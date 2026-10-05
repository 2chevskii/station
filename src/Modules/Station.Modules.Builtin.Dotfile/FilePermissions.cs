using System.Security.AccessControl;

namespace Station.Modules.Builtin.Dotfile;

internal static class FilePermissions
{
    public static FileStream CreateNew( string path, string? permissionsSource = null )
    {
        if ( OperatingSystem.IsWindows() && permissionsSource is not null )
        {
            var permissions = new FileInfo( permissionsSource )
                .GetAccessControl( AccessControlSections.Access );
            return new FileInfo( path ).Create(
                FileMode.CreateNew,
                FileSystemRights.Write,
                FileShare.None,
                4096,
                FileOptions.None,
                permissions
            );
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if ( !OperatingSystem.IsWindows() && permissionsSource is not null )
            options.UnixCreateMode = File.GetUnixFileMode( permissionsSource );

        return new FileStream( path, options );
    }

    public static void Copy( string source, string destination )
    {
        if ( OperatingSystem.IsWindows() )
        {
            var original = new FileInfo( source ).GetAccessControl( AccessControlSections.Access );
            var permissions = new FileSecurity();
            permissions.SetSecurityDescriptorBinaryForm(
                original.GetSecurityDescriptorBinaryForm(),
                AccessControlSections.Access
            );
            new FileInfo( destination ).SetAccessControl( permissions );
        }
        else
        {
            File.SetUnixFileMode( destination, File.GetUnixFileMode( source ) );
        }
    }
}
