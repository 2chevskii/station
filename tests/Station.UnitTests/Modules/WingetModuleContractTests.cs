using FluentAssertions;

using Station.Abstractions.Execution;
using Station.Abstractions.Processes;
using Station.Modules.Builtin.Winget;
using Station.Testing;

namespace Station.UnitTests.Modules;

public sealed class WingetModuleContractTests
{
    [Theory]
    [InlineData( "1.0", "2.0", "1.0", false )]
    [InlineData( "1.0", "2.0", "2.0", true )]
    [InlineData( "11.0", "12.0", "1.0", true )]
    [InlineData( "1.0 beta", "2.0", "1.0", true )]
    [InlineData( "1.0 beta", "2.0", "1.0 beta", false )]
    public async Task Check_compares_the_complete_installed_version(
        string installedVersion,
        string availableVersion,
        string requestedVersion,
        bool changed
    )
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, CreateTable( "Demo.Tool", installedVersion, availableVersion ), "" )
        );
        var module = CreateModule( runner, ExecutionMode.Check );
        module.Version = requestedVersion;

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().Be( changed );
        runner.Commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "No table was returned for Demo.Tool 1.0" )]
    [InlineData( "Name Id Version\n---------------\nTool Demo.Tool.Extra 1.0" )]
    public async Task Unrecognized_success_output_fails_without_modifying_packages( string output )
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
    public async Task Source_names_with_spaces_do_not_become_part_of_the_installed_version()
    {
        var output = CreateTable( "Demo.Tool", "1.0 beta", "2.0" )
            .Replace( "winget", "Private Package Feed", StringComparison.Ordinal );
        var runner = new RecordingProcessRunner( new ProcessResult( 0, output, "" ) );
        var module = CreateModule( runner, ExecutionMode.Check );
        module.Version = "1.0 beta";

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().BeFalse();
    }

    [Fact]
    public async Task Recognized_missing_package_exit_code_allows_installation()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( unchecked((int)0x8A150014), "No installed package found.", "" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( runner, ExecutionMode.Apply );

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().BeTrue();
        runner.Commands[1].Arguments.Should().ContainInOrder( "install", "--id", "Demo.Tool" );
    }

    [Fact]
    public async Task Requested_scope_applies_to_query_and_uninstall()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, CreateTable( "Demo.Tool", "1.0", "2.0" ), "" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( runner, ExecutionMode.Apply );
        module.State = "absent";
        module.Scope = "machine";

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().BeTrue();
        runner.Commands.Should().HaveCount( 2 );
        foreach ( var command in runner.Commands )
            command.Arguments.Should().ContainInOrder( "--scope", "machine" );
    }

    [Fact]
    public async Task Ambiguous_installed_records_fail_without_changing_packages()
    {
        var output = CreateTable( "Demo.Tool", "1.0", "2.0" )
            + $"{"Other copy",-12} {"Demo.Tool",-12} {"1.0",-12} {"2.0",-12} winget\n";
        var runner = new RecordingProcessRunner( new ProcessResult( 0, output, "" ) );
        var module = CreateModule( runner, ExecutionMode.Apply );
        module.State = "absent";

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeTrue();
        result.Message.Should().Contain( "ambiguous" );
        runner.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Invalid_scope_is_rejected_before_running_commands()
    {
        var runner = new RecordingProcessRunner();
        var module = CreateModule( runner, ExecutionMode.Apply );
        module.Scope = "all";

        (await module.InspectAsync( CancellationToken.None )).IsValid.Should().BeFalse();
        (await module.ExecuteAsync( CancellationToken.None )).Failed.Should().BeTrue();
        runner.Commands.Should().BeEmpty();
    }

    private static string CreateTable( string id, string installed, string available ) =>
        $"{"Name",-12} {"Id",-12} {"Version",-12} {"Available",-12} Source\n"
        + new string( '-', 59 ) + "\n"
        + $"{"Tool",-12} {id,-12} {installed,-12} {available,-12} winget\n";

    private static WingetModule CreateModule( IProcessRunner runner, ExecutionMode mode ) =>
        new WingetModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: mode
            ),
            runner
        )
        {
            PackageId = "Demo.Tool",
        };
}
