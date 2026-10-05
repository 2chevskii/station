using FluentAssertions;

using Station.Abstractions.Execution;
using Station.Abstractions.Processes;
using Station.Modules.Builtin.Apt;
using Station.Testing;

namespace Station.UnitTests.Modules;

public sealed class AptModuleContractTests
{
    [Theory]
    [InlineData( "installed\t1.0", "present", false )]
    [InlineData( "installed\t1.0", "absent", true )]
    [InlineData( "config-files\t1.0", "present", true )]
    [InlineData( "config-files\t1.0", "absent", false )]
    [InlineData( "not-installed\t", "present", true )]
    [InlineData( "not-installed\t", "absent", false )]
    [InlineData( "half-installed\t1.0", "present", true )]
    [InlineData( "half-installed\t1.0", "absent", true )]
    public async Task Check_uses_dpkg_status_to_detect_required_changes(
        string output,
        string state,
        bool changed
    )
    {
        var runner = new RecordingProcessRunner( new ProcessResult( 0, output, "" ) );
        var module = CreateModule( runner, ExecutionMode.Check );
        module.State = state;
        module.Version = "1.0";

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().Be( changed );
        runner.Commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "1.0" )]
    [InlineData( "unknown\t1.0" )]
    [InlineData( "installed\t" )]
    [InlineData( "installed\t1.0\ninstalled\t2.0" )]
    public async Task Invalid_query_output_fails_without_modifying_packages( string output )
    {
        var runner = new RecordingProcessRunner( new ProcessResult( 0, output, "" ) );
        var module = CreateModule( runner, ExecutionMode.Apply );

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeTrue();
        runner.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Missing_package_is_installed_using_direct_wsl_execution()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 1, "", "no packages found matching tool" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( runner, ExecutionMode.Apply );

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().BeTrue();
        runner.Commands.Should().HaveCount( 2 );
        runner.Commands[0].Arguments.Should().Contain( "-f=${db:Status-Status}\t${Version}\n" );
        if ( OperatingSystem.IsWindows() )
            runner.Commands.Should().OnlyContain( command => command.Arguments.Contains( "--exec" ) );

        runner.Commands[1].Arguments.Should().ContainInOrder( "apt-get", "install", "-y", "--", "tool" );
    }

    private static AptModule CreateModule( IProcessRunner runner, ExecutionMode mode ) =>
        new AptModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: mode
            ),
            runner
        )
        {
            PackageName = "tool",
            Distribution = "Ubuntu",
        };
}
