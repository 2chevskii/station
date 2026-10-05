using FluentAssertions;

using Station.Abstractions.FileSystem;
using Station.Testing;

namespace Station.IntegrationTests.Infrastructure;

[Collection( "Environment variables" )]
public sealed class CommandLocatorTests
{
    [Fact]
    public void Relative_command_paths_are_resolved_from_the_specified_working_directory()
    {
        using var directory = new TemporaryDirectory();
        var command = OperatingSystem.IsWindows() ? "tool.cmd" : "tool";
        directory.WriteFile( $"commands/{command}", "echo test" );
        var fullPath = directory.GetPath( "commands", command );
        if ( !OperatingSystem.IsWindows() )
            File.SetUnixFileMode( fullPath, UnixFileMode.UserRead | UnixFileMode.UserExecute );

        var found = CommandLocator.Find( $"./commands/{command}", directory.Path );

        found.Should().Be( fullPath );
    }

    [Fact]
    public void Windows_script_extensions_without_an_interpreter_are_not_executables()
    {
        if ( !OperatingSystem.IsWindows() )
            Assert.Skip( "Windows executable extension resolution is required." );

        using var shims = new CommandShimScope();
        File.WriteAllText( Path.Combine( shims.Path, "script-only.ps1" ), "Write-Output 'test'" );

        CommandLocator.Exists( "script-only" ).Should().BeFalse();
        CommandLocator.Exists( Path.Combine( shims.Path, "script-only.ps1" ) ).Should().BeFalse();
    }

    [Fact]
    public void Unix_commands_require_executable_permissions()
    {
        if ( OperatingSystem.IsWindows() )
        {
            Assert.Skip( "Unix executable permissions are required." );
            return;
        }

        using var shims = new CommandShimScope( includeHostPath: false );
        var command = Path.Combine( shims.Path, "permissions-probe" );
        File.WriteAllText( command, "#!/bin/sh\nexit 0\n" );
        File.SetUnixFileMode( command, UnixFileMode.UserRead | UnixFileMode.UserWrite );

        CommandLocator.Exists( "permissions-probe" ).Should().BeFalse();

        File.SetUnixFileMode(
            command,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );

        CommandLocator.Find( "permissions-probe" ).Should().Be( command );
    }
}
