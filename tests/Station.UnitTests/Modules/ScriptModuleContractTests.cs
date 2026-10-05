using FluentAssertions;

using Station.Abstractions.Processes;
using Station.Modules.Builtin.Script;
using Station.Testing;

namespace Station.UnitTests.Modules;

public sealed class ScriptModuleContractTests
{
    [Theory]
    [InlineData( "[]" )]
    [InlineData( "42" )]
    [InlineData( "null" )]
    [InlineData( "{\"changed\":\"true\"}" )]
    [InlineData( "{\"failed\":1}" )]
    [InlineData( "{\"skipped\":null}" )]
    [InlineData( "{\"message\":{}}" )]
    [InlineData( "{\"outputs\":[]}" )]
    [InlineData( "{\"outputs\":false}" )]
    [InlineData( "{\"changed\":false,\"changed\":true}" )]
    [InlineData( "{\"outputs\":{\"files\":[{\"name\":\"a\",\"name\":\"b\"}]}}" )]
    public async Task Invalid_response_shape_returns_a_failure( string output )
    {
        using var directory = new TemporaryDirectory();
        var runner = new RecordingProcessRunner( new ProcessResult( 0, output, "" ) );
        var module = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.Path ),
            runner
        )
        {
            ScriptPath = "example.ps1",
        };

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeTrue();
        result.Message.Should().Contain( "invalid JSON" );
        var command = runner.Commands.Should().ContainSingle().Subject;
        File.Exists( command.Environment!["CONFIG_TOOL_REQUEST_PATH"] ).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_fields_use_defaults_and_nullable_fields_accept_null()
    {
        using var directory = new TemporaryDirectory();
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, "{\"message\":null,\"outputs\":null,\"extension\":42}", "" )
        );
        var module = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.Path ),
            runner
        )
        {
            ScriptPath = "example.ps1",
        };

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse();
        result.Changed.Should().BeFalse();
        result.Skipped.Should().BeFalse();
        result.Message.Should().BeNull();
        result.Outputs.Should().BeNull();
    }

    [Fact]
    public async Task Failed_response_can_report_partial_changes_and_nested_outputs()
    {
        using var directory = new TemporaryDirectory();
        var runner = new RecordingProcessRunner(
            new ProcessResult(
                0,
                """
                {"changed":true,"failed":true,"message":"partial failure","outputs":{"files":["a"]}}
                """,
                ""
            )
        );
        var module = new ScriptModule(
            TestModuleContextFactory.Create( directory.Path, directory.Path ),
            runner
        )
        {
            ScriptPath = "example.ps1",
        };

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeTrue();
        result.Changed.Should().BeTrue();
        result.Message.Should().Be( "partial failure" );
        result.Outputs!["files"]![0]!.GetValue<string>().Should().Be( "a" );
    }
}
