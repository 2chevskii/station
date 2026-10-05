using System.Text.Json;

using FluentAssertions;

using Station.Abstractions.FileSystem;
using Station.Abstractions.Processes;
using Station.Testing;
using Station.Utilities.Processes;

namespace Station.IntegrationTests.Infrastructure;

[Collection( "Environment variables" )]
public sealed class CommandShimScopeTests
{
    [Fact]
    public void Isolated_scope_removes_host_commands_and_restores_environment()
    {
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        var originalExtensions = Environment.GetEnvironmentVariable( "PATHEXT" );
        using ( var shims = new CommandShimScope( includeHostPath: false ) )
        {
            Environment.GetEnvironmentVariable( "PATH" ).Should().Be( shims.Path );
            CommandLocator.Find( "apt-get" ).Should().BeNull();
            CommandLocator.Find( "sudo" ).Should().BeNull();
            CommandLocator.Find( "dpkg-query" ).Should().BeNull();
            CommandLocator.Find( "winget" ).Should().BeNull();
        }

        Environment.GetEnvironmentVariable( "PATH" ).Should().Be( originalPath );
        Environment.GetEnvironmentVariable( "PATHEXT" ).Should().Be( originalExtensions );
    }

    [Fact]
    public async Task PowerShell_fixture_preserves_arguments_without_host_PATH()
    {
        using var shims = new CommandShimScope( includeHostPath: false );
        var command = shims.AddPowerShellCommand(
            "arguments",
            "ConvertTo-Json -InputObject @($args) -Compress"
        );
        string[] arguments = ["with spaces", "literal$variable", "name=value=tail", "кириллица"];

        var result = await new ProcessRunner().RunAsync(
            new ProcessCommand( command, arguments ),
            CancellationToken.None
        );

        result.ExitCode.Should().Be( 0, result.StandardError );
        JsonSerializer.Deserialize<string[]>( result.StandardOutput ).Should().Equal( arguments );
    }
}
