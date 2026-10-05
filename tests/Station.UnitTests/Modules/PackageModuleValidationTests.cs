using FluentAssertions;

using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Abstractions.Processes;
using Station.Modules.Builtin.Apt;
using Station.Modules.Builtin.Scoop;
using Station.Modules.Builtin.Winget;
using Station.Testing;

namespace Station.UnitTests.Modules;

public sealed class PackageModuleValidationTests
{
    [Theory]
    [InlineData( "apt", "preesnt" )]
    [InlineData( "scoop", "preesnt" )]
    [InlineData( "winget", "preesnt" )]
    [InlineData( "apt", " " )]
    [InlineData( "scoop", " " )]
    [InlineData( "winget", " " )]
    [InlineData( "apt", null )]
    [InlineData( "scoop", null )]
    [InlineData( "winget", null )]
    public async Task Invalid_state_fails_before_querying_or_changing_packages(
        string provider,
        string? state
    )
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( 0, "installed tool Demo.Tool 1.0", "" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( provider, runner, ExecutionMode.Apply, state );

        var inspection = await module.InspectAsync( CancellationToken.None );
        var result = await module.ExecuteAsync( CancellationToken.None );

        inspection.IsValid.Should().BeFalse();
        inspection.Diagnostics.Should().Contain( diagnostic => diagnostic.Message.Contains( "state" ) );
        result.Failed.Should().BeTrue();
        result.Message.Should().Contain( "state" );
        runner.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData( "apt", " " )]
    [InlineData( "scoop", " " )]
    [InlineData( "winget", " " )]
    [InlineData( "apt", "tool\n" )]
    [InlineData( "scoop", "tool\n" )]
    [InlineData( "apt", "tool*" )]
    [InlineData( "scoop", "tool*" )]
    public async Task Invalid_package_identifier_fails_before_querying_packages(
        string provider,
        string packageName
    )
    {
        var runner = new RecordingProcessRunner();
        var module = CreateModule( provider, runner, ExecutionMode.Apply, "present", packageName );

        var inspection = await module.InspectAsync( CancellationToken.None );
        var result = await module.ExecuteAsync( CancellationToken.None );

        inspection.IsValid.Should().BeFalse();
        result.Failed.Should().BeTrue();
        runner.Commands.Should().BeEmpty();
    }

    [Theory]
    [MemberData( nameof( QueryFailureCases ) )]
    public async Task Query_errors_fail_instead_of_reporting_absence_or_changing_packages(
        string provider,
        ExecutionMode mode,
        string state,
        int exitCode
    )
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult( exitCode, "", "package database unavailable" ),
            new ProcessResult( 0, "", "" )
        );
        var module = CreateModule( provider, runner, mode, state );

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeTrue();
        result.Message.Should().Contain( "package database unavailable" );
        runner.Commands.Should().ContainSingle();
    }

    public static IEnumerable<object[]> QueryFailureCases()
    {
        foreach ( var provider in new[] { "apt", "scoop", "winget" } )
        {
            foreach ( var mode in new[] { ExecutionMode.Check, ExecutionMode.Apply } )
            {
                foreach ( var state in new[] { "present", "absent" } )
                    yield return [provider, mode, state, provider == "scoop" ? 1 : 2];
            }
        }
    }

    private static ConfigModule CreateModule(
        string provider,
        IProcessRunner runner,
        ExecutionMode mode,
        string? state,
        string? packageName = null
    )
    {
        var context = TestModuleContextFactory.Create(
            Environment.CurrentDirectory,
            Environment.CurrentDirectory,
            mode: mode
        );
        return provider switch
        {
            "apt" => new AptModule( context, runner )
            {
                PackageName = packageName ?? "tool",
                State = state!,
                Distribution = "Ubuntu",
            },
            "scoop" => new ScoopModule( context, runner )
            {
                PackageName = packageName ?? "tool",
                State = state!,
            },
            "winget" => new WingetModule( context, runner )
            {
                PackageId = packageName ?? "Demo.Tool",
                State = state!,
            },
            _ => throw new ArgumentException( "Unknown package module.", nameof( provider ) ),
        };
    }
}
