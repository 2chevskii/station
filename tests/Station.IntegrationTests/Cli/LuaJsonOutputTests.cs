using System.Text.Json;

using FluentAssertions;

using Station.Testing;

namespace Station.IntegrationTests.Cli;

[Collection( "Environment variables" )]
public sealed class LuaJsonOutputTests
{
    [Fact]
    public async Task Lua_print_writes_to_stderr_and_preserves_JSON_output()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "station.yaml", "roles: [base]\n" );
        directory.WriteFile( "vars/common.lua", "print('Lua variables', 42); return {}" );
        directory.WriteFile( "roles/base/role.lua", "print('Lua role', true)" );
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await Station.Program.RunAsync(
            ["validate", "--config", directory.Path, "--output", "json"],
            null,
            output,
            error
        );

        exitCode.Should().Be( 0 );
        using var json = JsonDocument.Parse( output.ToString() );
        json.RootElement.GetProperty( "succeeded" ).GetBoolean().Should().BeTrue();
        error.ToString().Should().ContainAll( "Lua variables\t42", "Lua role\ttrue" );
    }
}
