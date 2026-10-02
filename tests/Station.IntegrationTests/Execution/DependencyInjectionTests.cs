using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Station.Abstractions.Artifacts;
using Station.Abstractions.Configuration;
using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Core.Execution.Modules;
using Station.Core.Execution.Running;
using Station.IntegrationTests.Infrastructure;
using Station.Testing;
using Station.Utilities.Privileges;

namespace Station.IntegrationTests.Execution;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task Modules_receive_registered_dependencies_and_a_fresh_context_for_each_task()
    {
        using var directory = CreateConfiguration();
        var clock = new FakeTimeProvider( DateTimeOffset.Parse( "2026-10-01T12:00:00Z" ) );
        var probe = new DependencyProbe();
        using var services = IntegrationServices.Create( registrations =>
        {
            registrations.AddSingleton<TimeProvider>( clock );
            registrations.AddSingleton<IPrivilegeDetector, ElevatedPrivilegeDetector>();
            registrations.AddSingleton( probe );
            registrations.AddSingleton(
                new ModuleRegistration( "tests", typeof( InjectedModule ) )
            );
        } );
        var engine = services.GetRequiredService<ConfigEngine>();

        var result = await engine.RunAsync( directory.Path, ExecutionMode.Check );

        result.Succeeded.Should().BeTrue( string.Join( "; ", result.Diagnostics ) );
        probe.Contexts.Should().HaveCount( 2 );
        probe.Contexts[0].Should().NotBeSameAs( probe.Contexts[1] );
        probe.Contexts.Select( context => context.TaskName ).Should().Equal( "first", "second" );
        probe.Contexts.Should().OnlyContain( context =>
            context.TimeProvider == clock && context.IsElevated && context.Mode == ExecutionMode.Check
        );
        probe.Clocks.Should().OnlyContain( timeProvider => timeProvider == clock );
        probe.Values.Should().Equal( "configured", "default" );
    }

    [Fact]
    public async Task Explain_uses_registered_module_dependencies_without_executing_tasks()
    {
        using var directory = CreateConfiguration();
        var probe = new DependencyProbe();
        using var services = IntegrationServices.Create( registrations =>
        {
            registrations.AddSingleton( probe );
            registrations.AddSingleton(
                new ModuleRegistration( "tests", typeof( InjectedModule ) )
            );
        } );

        var result = await services.GetRequiredService<ConfigEngine>().ExplainAsync( directory.Path );

        result.Succeeded.Should().BeTrue( string.Join( "; ", result.Diagnostics ) );
        probe.Contexts.Should().HaveCount( 2 );
        probe.Contexts.Should().OnlyContain( context => context.Mode == ExecutionMode.Validate );
        probe.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_module_dependency_is_reported_by_validate_and_explain()
    {
        using var directory = CreateConfiguration();
        using var services = IntegrationServices.Create( registrations =>
            registrations.AddSingleton(
                new ModuleRegistration( "tests", typeof( InjectedModule ) )
            )
        );
        var engine = services.GetRequiredService<ConfigEngine>();

        var validation = await engine.RunAsync( directory.Path, ExecutionMode.Validate );
        var explanation = await engine.ExplainAsync( directory.Path );

        validation.Succeeded.Should().BeFalse();
        validation.Diagnostics.Should().Contain( diagnostic =>
            diagnostic.Contains( nameof( DependencyProbe ) )
        );
        explanation.Succeeded.Should().BeFalse();
        explanation.Diagnostics.Should().Contain( diagnostic =>
            diagnostic.Contains( nameof( DependencyProbe ) )
        );
    }

    [Fact]
    public async Task Registered_template_engine_is_used_for_cache_directory_and_task_parameters()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "station.yaml",
            """
            roles: [empty]
            vars:
              plugin_cache_dir: "{{ injected }}"
            tasks:
              - name: rendered
                tests.injected:
                  value: "{{ injected }}"
            """
        );
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        var renderer = new InjectedTemplateEngine();
        var probe = new DependencyProbe();
        using var services = IntegrationServices.Create( registrations =>
        {
            registrations.AddSingleton<ITemplateEngine>( renderer );
            registrations.AddSingleton( probe );
            registrations.AddSingleton(
                new ModuleRegistration( "tests", typeof( InjectedModule ) )
            );
        } );

        var result = await services.GetRequiredService<ConfigEngine>().RunAsync(
            directory.Path,
            ExecutionMode.Check
        );

        result.Succeeded.Should().BeTrue( string.Join( "; ", result.Diagnostics ) );
        renderer.Sources.Should().Contain( "plugin_cache_dir" );
        probe.Values.Should().Equal( "from-di" );
        probe.Contexts.Should().OnlyContain( context => context.TemplateEngine == renderer );
    }

    private static TemporaryDirectory CreateConfiguration()
    {
        var directory = new TemporaryDirectory();
        directory.WriteFile(
            "station.yaml",
            """
            roles: [empty]
            tasks:
              - name: first
                tests.injected:
                  value: configured
              - name: second
                tests.injected: {}
            """
        );
        directory.WriteFile( "roles/empty/role.yaml", "[]" );
        return directory;
    }

    public sealed class DependencyProbe
    {
        public List<ModuleContext> Contexts { get; } = [];
        public List<TimeProvider> Clocks { get; } = [];
        public List<string> Values { get; } = [];
    }

    [Module( "injected" )]
    public sealed class InjectedModule : ConfigModule
    {
        private readonly DependencyProbe probe;

        public InjectedModule( ModuleContext context, DependencyProbe probe, TimeProvider timeProvider )
            : base( context )
        {
            this.probe = probe;
            probe.Contexts.Add( context );
            probe.Clocks.Add( timeProvider );
        }

        [Parameter( "value" )]
        public string Value { get; set; } = "default";

        public override ValueTask<ModuleResult> ExecuteAsync( CancellationToken cancellationToken )
        {
            probe.Values.Add( Value );
            return ValueTask.FromResult( ModuleResult.NoChange() );
        }
    }

    private sealed class ElevatedPrivilegeDetector : IPrivilegeDetector
    {
        public bool IsElevated() => true;
    }

    private sealed class InjectedTemplateEngine : ITemplateEngine
    {
        public List<string?> Sources { get; } = [];

        public string Render( string template, VariableSet variables, string? sourceName = null )
        {
            Sources.Add( sourceName );
            return template.Replace( "{{ injected }}", "from-di", StringComparison.Ordinal );
        }
    }
}
