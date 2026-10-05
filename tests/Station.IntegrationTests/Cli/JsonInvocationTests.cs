using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Station.Abstractions.Modules;
using Station.Cli.Commands;
using Station.Core.Execution.Modules;
using Station.IntegrationTests.Infrastructure;
using Station.Testing;
using Station.Utilities.Privileges;

namespace Station.IntegrationTests.Cli;

[Collection( "Environment variables" )]
public sealed class JsonInvocationTests
{
    [Fact]
    public async Task Unexpected_runtime_error_returns_JSON_and_restores_console_writers()
    {
        using var directory = CreateConfiguration( "tests.noisy" );
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var services = IntegrationServices.Create( registrations =>
        {
            registrations.AddKeyedSingleton<TextWriter>( "stdout", output );
            registrations.AddKeyedSingleton<TextWriter>( "stderr", error );
            registrations.AddSingleton<IPrivilegeDetector, FailingPrivilegeDetector>();
            registrations.AddSingleton( new ModuleRegistration( "tests", typeof( NoisyModule ) ) );
        } );
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        var exitCode = await services.GetRequiredService<StationCommandLine>().RunAsync(
            ["check", "--config", directory.Path, "--output", "json"]
        );

        exitCode.Should().Be( 1 );
        using var json = JsonDocument.Parse( output.ToString() );
        json.RootElement.GetProperty( "message" ).GetString().Should().Be( "privilege lookup failed" );
        json.RootElement.GetProperty( "succeeded" ).GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty( "data" ).ValueKind.Should().Be( JsonValueKind.Null );
        Console.Out.Should().BeSameAs( originalOutput );
        Console.Error.Should().BeSameAs( originalError );
    }

    [Fact]
    public async Task Managed_module_output_is_routed_to_stderr_and_console_writers_are_restored()
    {
        using var directory = CreateConfiguration( "tests.noisy" );
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var services = IntegrationServices.Create( registrations =>
        {
            registrations.AddKeyedSingleton<TextWriter>( "stdout", output );
            registrations.AddKeyedSingleton<TextWriter>( "stderr", error );
            registrations.AddSingleton( new ModuleRegistration( "tests", typeof( NoisyModule ) ) );
        } );
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        var exitCode = await services.GetRequiredService<StationCommandLine>().RunAsync(
            ["check", "--config", directory.Path, "--output", "json"]
        );

        exitCode.Should().Be( 0 );
        using var json = JsonDocument.Parse( output.ToString() );
        json.RootElement.GetProperty( "succeeded" ).GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty( "diagnostics" ).ToString().Should().Contain( "inspection warning" );
        error.ToString().Should().ContainAll( "module stdout", "module stderr", "Inspecting" );
        Console.Out.Should().BeSameAs( originalOutput );
        Console.Error.Should().BeSameAs( originalError );
    }

    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public async Task Cooperative_cancellation_returns_JSON_and_exit_code_130( bool cancelBeforeStart )
    {
        using var directory = CreateConfiguration( "tests.wait" );
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var probe = new CancellationProbe();
        using var services = IntegrationServices.Create( registrations =>
        {
            registrations.AddKeyedSingleton<TextWriter>( "stdout", output );
            registrations.AddKeyedSingleton<TextWriter>( "stderr", error );
            registrations.AddSingleton( probe );
            registrations.AddSingleton( new ModuleRegistration( "tests", typeof( WaitingModule ) ) );
        } );
        if ( cancelBeforeStart )
            cancellation.Cancel();

        var originalOutput = Console.Out;
        var originalError = Console.Error;
        var invocation = services.GetRequiredService<StationCommandLine>().RunAsync(
            ["apply", "--config", directory.Path, "--output", "json"],
            cancellation.Token
        );

        try
        {
            if ( !cancelBeforeStart )
            {
                await probe.Started.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                cancellation.Cancel();
            }

            var exitCode = await invocation.WaitAsync( TimeSpan.FromSeconds( 5 ) );

            exitCode.Should().Be( 130 );
            using var json = JsonDocument.Parse( output.ToString() );
            json.RootElement.GetProperty( "succeeded" ).GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty( "exitCode" ).GetInt32().Should().Be( 130 );
            json.RootElement.GetProperty( "message" ).GetString().Should().Be( "Operation cancelled." );
            json.RootElement.GetProperty( "data" ).ValueKind.Should().Be( JsonValueKind.Null );
            probe.Started.Task.IsCompleted.Should().Be( !cancelBeforeStart );
            Console.Out.Should().BeSameAs( originalOutput );
            Console.Error.Should().BeSameAs( originalError );
        }
        finally
        {
            cancellation.Cancel();
            await invocation.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        }
    }

    private static TemporaryDirectory CreateConfiguration( string module )
    {
        var directory = new TemporaryDirectory();
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        directory.WriteFile( "station.yaml", $"roles: [empty]\ntasks:\n  - {module}: {{}}\n" );
        return directory;
    }

    [Module( "noisy" )]
    public sealed class NoisyModule( ModuleContext context ) : ConfigModule( context )
    {
        public override ValueTask<ModuleInspectionResult> InspectAsync( CancellationToken cancellationToken )
        {
            Console.WriteLine( "module stdout" );
            Console.Error.WriteLine( "module stderr" );
            return ValueTask.FromResult( new ModuleInspectionResult(
                PrivilegeRequirement.User,
                [new ModuleDiagnostic( "inspection warning", DiagnosticSeverity.Warning )]
            ) );
        }

        public override ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken ) =>
            ValueTask.FromResult( ModuleResult.NoChange() );
    }

    public sealed class CancellationProbe
    {
        public TaskCompletionSource Started { get; } = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
    }

    [Module( "wait" )]
    public sealed class WaitingModule( ModuleContext context, CancellationProbe probe ) : ConfigModule( context )
    {
        public override async ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken )
        {
            probe.Started.SetResult();
            await Task.Delay( Timeout.InfiniteTimeSpan, cancellationToken );
            return ModuleResult.NoChange();
        }
    }

    private sealed class FailingPrivilegeDetector : IPrivilegeDetector
    {
        public bool IsElevated() => throw new InvalidOperationException( "privilege lookup failed" );
    }
}
