using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Core.Execution.Modules;
using Station.Core.Execution.Running;
using Station.IntegrationTests.Infrastructure;
using Station.Testing;

namespace Station.IntegrationTests.Execution;

public sealed class TaskExecutionTests
{
    [Theory]
    [InlineData( ExecutionMode.Check, "missing.module: {}" )]
    [InlineData( ExecutionMode.Apply, "missing.module: {}" )]
    [InlineData( ExecutionMode.Check, "tests.probe: { unknown: value }" )]
    [InlineData( ExecutionMode.Apply, "tests.probe: { unknown: value }" )]
    public async Task Resolution_and_binding_errors_stop_execution(
        ExecutionMode mode,
        string middleTask
    )
    {
        using var directory = CreateConfiguration( middleTask );
        var probe = new ExecutionProbe();
        using var services = CreateServices( probe );

        var result = await services.GetRequiredService<ConfigEngine>().RunAsync( directory.Path, mode );

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain( diagnostic => diagnostic.StartsWith( "second: error:" ) );
        result.Tasks.Select( task => task.TaskName ).Should().Equal( "first" );
        probe.InspectedTasks.Should().Equal( "first" );
        probe.ExecutedTasks.Should().Equal( "first" );
    }

    [Theory]
    [InlineData( ExecutionMode.Check, false )]
    [InlineData( ExecutionMode.Apply, false )]
    [InlineData( ExecutionMode.Check, true )]
    [InlineData( ExecutionMode.Apply, true )]
    public async Task Failed_inspection_stops_execution_and_preserves_diagnostics(
        ExecutionMode mode,
        bool throwException
    )
    {
        using var directory = CreateConfiguration();
        var probe = new ExecutionProbe
        {
            Inspect = ( task, _ ) =>
            {
                if ( task != "second" )
                    return Warning();

                if ( throwException )
                    throw new InvalidOperationException( "inspection error" );

                return ModuleInspectionResult.Failure( new ModuleDiagnostic( "inspection error" ) );
            },
        };
        using var services = CreateServices( probe );

        var result = await services.GetRequiredService<ConfigEngine>().RunAsync( directory.Path, mode );

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain( "first: warning: inspection warning" );
        result.Diagnostics.Should().Contain( diagnostic => diagnostic.Contains( "inspection error" ) );
        result.Tasks.Select( task => task.TaskName ).Should().Equal( "first" );
        probe.InspectedTasks.Should().Equal( "first", "second" );
        probe.ExecutedTasks.Should().Equal( "first" );
    }

    [Fact]
    public async Task Validate_collects_errors_and_inspects_remaining_tasks_without_executing()
    {
        using var directory = CreateConfiguration( "missing.module: {}" );
        var probe = new ExecutionProbe
        {
            Inspect = ( task, _ ) => task == "first"
                ? ModuleInspectionResult.Failure( new ModuleDiagnostic( "inspection error" ) )
                : Warning(),
        };
        using var services = CreateServices( probe );

        var result = await services.GetRequiredService<ConfigEngine>().RunAsync(
            directory.Path,
            ExecutionMode.Validate
        );

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().HaveCount( 3 );
        result.Diagnostics.Should().Contain( "first: error: inspection error" );
        result.Diagnostics.Should().Contain( diagnostic => diagnostic.StartsWith( "second: error:" ) );
        result.Diagnostics.Should().Contain( "third: warning: inspection warning" );
        result.Tasks.Should().BeEmpty();
        probe.InspectedTasks.Should().Equal( "first", "third" );
        probe.ExecutedTasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData( ExecutionMode.Validate )]
    [InlineData( ExecutionMode.Check )]
    [InlineData( ExecutionMode.Apply )]
    public async Task Successful_run_preserves_warnings( ExecutionMode mode )
    {
        using var directory = CreateConfiguration();
        var probe = new ExecutionProbe { Inspect = ( _, _ ) => Warning() };
        using var services = CreateServices( probe );

        var result = await services.GetRequiredService<ConfigEngine>().RunAsync( directory.Path, mode );

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().Equal(
            "first: warning: inspection warning",
            "second: warning: inspection warning",
            "third: warning: inspection warning"
        );
    }

    [Theory]
    [InlineData( ExecutionMode.Check, false )]
    [InlineData( ExecutionMode.Apply, false )]
    [InlineData( ExecutionMode.Check, true )]
    [InlineData( ExecutionMode.Apply, true )]
    public async Task Execution_failure_preserves_previous_records_and_diagnostics(
        ExecutionMode mode,
        bool throwException
    )
    {
        using var directory = CreateConfiguration();
        var probe = new ExecutionProbe
        {
            Inspect = ( _, _ ) => Warning(),
            Execute = ( task, _ ) =>
            {
                if ( task != "second" )
                    return ModuleResult.ChangedResult();

                if ( throwException )
                    throw new InvalidOperationException( "execution error" );

                return ModuleResult.Failure( "execution error" );
            },
        };
        using var services = CreateServices( probe );

        var result = await services.GetRequiredService<ConfigEngine>().RunAsync( directory.Path, mode );

        result.Succeeded.Should().BeFalse();
        result.ChangedCount.Should().Be( 1 );
        result.Tasks.Select( task => task.TaskName ).Should().Equal( "first", "second" );
        result.Tasks[1].Result.Failed.Should().BeTrue();
        result.Diagnostics.Should().Contain( "first: warning: inspection warning" );
        result.Diagnostics.Should().Contain( "second: warning: inspection warning" );
        result.Diagnostics.Should().Contain( diagnostic => diagnostic.Contains( "execution error" ) );
        probe.ExecutedTasks.Should().Equal( "first", "second" );
    }

    [Theory]
    [InlineData( ExecutionMode.Validate, false, false )]
    [InlineData( ExecutionMode.Validate, false, true )]
    [InlineData( ExecutionMode.Check, false, false )]
    [InlineData( ExecutionMode.Check, false, true )]
    [InlineData( ExecutionMode.Apply, false, false )]
    [InlineData( ExecutionMode.Apply, false, true )]
    [InlineData( ExecutionMode.Check, true, false )]
    [InlineData( ExecutionMode.Check, true, true )]
    [InlineData( ExecutionMode.Apply, true, false )]
    [InlineData( ExecutionMode.Apply, true, true )]
    public async Task Cancellation_propagates_and_stops_remaining_work(
        ExecutionMode mode,
        bool cancelDuringExecution,
        bool throwException
    )
    {
        using var directory = CreateConfiguration();
        using var cancellation = new CancellationTokenSource();
        var probe = new ExecutionProbe
        {
            Inspect = ( _, token ) =>
            {
                if ( !cancelDuringExecution )
                {
                    cancellation.Cancel();
                    if ( throwException )
                        token.ThrowIfCancellationRequested();
                }

                return ModuleInspectionResult.Success();
            },
            Execute = ( _, token ) =>
            {
                cancellation.Cancel();
                if ( throwException )
                    token.ThrowIfCancellationRequested();

                return ModuleResult.NoChange();
            },
        };
        using var services = CreateServices( probe );

        var action = () => services.GetRequiredService<ConfigEngine>().RunAsync(
            directory.Path,
            mode,
            cancellationToken: cancellation.Token
        );

        await action.Should().ThrowAsync<OperationCanceledException>();
        probe.InspectedTasks.Should().Equal( "first" );
        probe.ExecutedTasks.Should().Equal( cancelDuringExecution ? ["first"] : [] );
    }

    [Theory]
    [InlineData( "validate" )]
    [InlineData( "check" )]
    [InlineData( "apply" )]
    [InlineData( "restore" )]
    [InlineData( "explain" )]
    [InlineData( "roles" )]
    [InlineData( "modules" )]
    public async Task Pre_cancelled_operations_do_not_load_configuration( string operation )
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var services = IntegrationServices.Create();
        var engine = services.GetRequiredService<ConfigEngine>();
        Func<Task> action = () => operation switch
        {
            "restore" => engine.RestoreAsync( directory.Path, cancellationToken: cancellation.Token ),
            "explain" => engine.ExplainAsync( directory.Path, cancellationToken: cancellation.Token ),
            "roles" => engine.ListRolesAsync( directory.Path, cancellationToken: cancellation.Token ),
            "modules" => engine.ListModulesAsync(
                directory.Path,
                cancellationToken: cancellation.Token
            ),
            _ => engine.RunAsync(
                directory.Path,
                Enum.Parse<ExecutionMode>( operation, true ),
                cancellationToken: cancellation.Token
            ),
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static TemporaryDirectory CreateConfiguration( string middleTask = "tests.probe: {}" )
    {
        var directory = new TemporaryDirectory();
        directory.WriteFile(
            "station.yaml",
            $$"""
              roles: [empty]
              tasks:
                - name: first
                  tests.probe: {}
                - name: second
                  {{middleTask}}
                - name: third
                  tests.probe: {}
              """
        );
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        return directory;
    }

    private static ServiceProvider CreateServices( ExecutionProbe probe ) =>
        IntegrationServices.Create( registrations =>
        {
            registrations.AddSingleton( probe );
            registrations.AddSingleton( new ModuleRegistration( "tests", typeof( ProbeModule ) ) );
        } );

    private static ModuleInspectionResult Warning() =>
        new ModuleInspectionResult(
            PrivilegeRequirement.User,
            [new ModuleDiagnostic( "inspection warning", DiagnosticSeverity.Warning )]
        );

    public sealed class ExecutionProbe
    {
        public List<string> InspectedTasks { get; } = [];
        public List<string> ExecutedTasks { get; } = [];
        public Func<string, CancellationToken, ModuleInspectionResult>? Inspect { get; init; }
        public Func<string, CancellationToken, ModuleResult>? Execute { get; init; }
    }

    [Module( "probe" )]
    public sealed class ProbeModule( ModuleContext context, ExecutionProbe probe )
        : ConfigModule( context )
    {
        public override ValueTask<ModuleInspectionResult> InspectAsync(
            CancellationToken cancellationToken
        )
        {
            probe.InspectedTasks.Add( Context.TaskName );
            return ValueTask.FromResult(
                probe.Inspect?.Invoke( Context.TaskName, cancellationToken )
                ?? ModuleInspectionResult.Success()
            );
        }

        public override ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken )
        {
            probe.ExecutedTasks.Add( Context.TaskName );
            return ValueTask.FromResult(
                probe.Execute?.Invoke( Context.TaskName, cancellationToken ) ?? ModuleResult.NoChange()
            );
        }
    }
}
