using FluentAssertions;

using Station.Abstractions.Configuration;
using Station.Configuration.Yaml;

namespace Station.IntegrationTests.Configuration;

public sealed class ScribanVariableTests
{
    [Fact]
    public void Render_resolves_nested_templates_references_and_arrays_on_access()
    {
        var variables = new VariableSet( new ConfigObject(
        [
            KeyValuePair.Create<string, ConfigValue>( "base", ConfigScalar.String( "/home/demo" ) ),
            KeyValuePair.Create<string, ConfigValue>( "directory", new TemplateMarkup( "{{ base }}/editor" ) ),
            KeyValuePair.Create<string, ConfigValue>( "nested", new ConfigObject(
            [
                KeyValuePair.Create<string, ConfigValue>( "file", new TemplateMarkup( "{{ directory }}/settings.json" ) ),
            ] ) ),
            KeyValuePair.Create<string, ConfigValue>( "alias", new VariableReference( "nested.file" ) ),
            KeyValuePair.Create<string, ConfigValue>( "items", new ConfigArray(
            [
                new VariableReference( "directory" ),
                new TemplateMarkup( "{{ base }}/other" ),
            ] ) ),
        ] ) );
        var renderer = new ScribanTemplateRenderer();

        renderer.Render( "{{ alias }}", variables ).Should().Be( "/home/demo/editor/settings.json" );
        renderer.Render( "{{ for item in items }}{{ item }};{{ end }}", variables )
            .Should().Be( "/home/demo/editor;/home/demo/other;" );
    }

    [Fact]
    public void Render_preserves_object_iteration_and_template_local_assignments()
    {
        var variables = new VariableSet( new ConfigObject(
        [
            KeyValuePair.Create<string, ConfigValue>( "nested", new ConfigObject(
            [
                KeyValuePair.Create<string, ConfigValue>( "name", ConfigScalar.String( "demo" ) ),
            ] ) ),
        ] ) );
        var renderer = new ScribanTemplateRenderer();

        renderer.Render( "{{ for pair in nested }}{{ pair.key }}={{ pair.value }}{{ end }}", variables )
            .Should().Be( "name=demo" );
        renderer.Render( "{{ local_value = 'local' }}{{ local_value }}", variables ).Should().Be( "local" );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void Render_rejects_reference_and_template_cycles( bool useTemplates )
    {
        ConfigValue first = useTemplates ? new TemplateMarkup( "{{ second }}" ) : new VariableReference( "second" );
        ConfigValue second = useTemplates ? new TemplateMarkup( "{{ first }}" ) : new VariableReference( "first" );
        var variables = new VariableSet( new ConfigObject(
        [
            KeyValuePair.Create( "first", first ),
            KeyValuePair.Create( "second", second ),
        ] ) );
        var renderer = new ScribanTemplateRenderer();

        var action = () => renderer.Render( "{{ first }}", variables );
        action.Should().Throw<ConfigurationException>().WithMessage( "*cycle detected*" );
    }
}
