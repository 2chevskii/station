using FluentAssertions;

using Station.Abstractions.Execution;
using Station.Abstractions.Processes;
using Station.Modules.Builtin.Scoop;
using Station.Testing;

namespace Station.UnitTests.Modules;

public sealed class ScoopModuleContractTests
{
    [Theory]
    [InlineData( "tool", "", false, false )]
    [InlineData( "tool-helper", "", false, true )]
    [InlineData( "tool", "Global install", false, true )]
    [InlineData( "tool", "", true, true )]
    [InlineData( "tool", "Global install, Held package", true, false )]
    public async Task Check_matches_exact_package_name_and_install_scope(
        string installedName,
        string info,
        bool global,
        bool changed
    )
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, $$"""{"apps":[{"Name":"{{installedName}}","Info":"{{info}}"}]}""", "" )
        );
        var module = CreateModule( runner, ExecutionMode.Check );
        module.IsGlobal = global;

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().Be( changed );
        runner.Commands.Should().ContainSingle().Which.Arguments.Should().Equal( "export" );
    }

    [Theory]
    [InlineData( "{}" )]
    [InlineData( "{\"apps\":null}" )]
    [InlineData( "{\"apps\":[{}]}" )]
    [InlineData( "{\"apps\":[{\"Name\":\"tool\",\"Info\":42}]}" )]
    [InlineData( "{\"apps\":[{\"Name\":\"tool\",\"Info\":\"Install failed\"}]}" )]
    [InlineData( "not json" )]
    public async Task Invalid_inventory_fails_without_modifying_packages( string output )
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, output, "" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( runner, ExecutionMode.Apply );

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeTrue();
        runner.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Bucket_qualified_package_is_removed_by_name_in_the_requested_scope()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, """{"apps":[{"Name":"tool","Info":"Global install"}]}""", "" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( runner, ExecutionMode.Apply );
        module.PackageName = "main/tool";
        module.State = "absent";
        module.IsGlobal = true;

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().BeTrue();
        runner.Commands[1].Arguments.Should().Equal( "uninstall", "--global", "tool" );
    }

    [Fact]
    public async Task Empty_inventory_allows_installation_and_preserves_bucket_qualification()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, "{\"apps\":[]}", "" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( runner, ExecutionMode.Apply );
        module.PackageName = "main/tool";

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().BeTrue();
        runner.Commands[1].Arguments.Should().Equal( "install", "main/tool" );
    }

    private static ScoopModule CreateModule( IProcessRunner runner, ExecutionMode mode ) =>
        new ScoopModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: mode
            ),
            runner
        )
        {
            PackageName = "tool",
        };
}
