using System.Text.Json.Nodes;

using FluentAssertions;

using Station.Abstractions.Configuration;
using Station.Testing;

namespace Station.UnitTests.Abstractions;

public sealed class ConfigValueUtilitiesTests
{
    [Fact]
    public void EnsureValue_returns_null_singleton_for_null()
    {
        ConfigValueUtilities.EnsureValue( null ).Should().BeSameAs( ConfigNull.Instance );
    }

    [Theory]
    [InlineData( "hello {{ name }}", true )]
    [InlineData( "{% if value %}", true )]
    [InlineData( "plain text", false )]
    public void IsTemplateMarkup_detects_template_syntax( string value, bool expected )
    {
        ConfigValueUtilities.IsTemplateMarkup( value ).Should().Be( expected );
    }

    [Fact]
    public void SplitPath_ignores_empty_segments()
    {
        ConfigValueUtilities.SplitPath( " user..name " ).Should().Equal( "user", "name" );
    }

    [Fact]
    public void Merge_recursively_merges_objects_and_replaces_scalars()
    {
        var current = new ConfigObject(
            new Dictionary<string, ConfigValue>
            {
                ["name"] = ConfigScalar.String( "old" ),
                ["nested"] = new ConfigObject(
                    new Dictionary<string, ConfigValue>
                    {
                        ["enabled"] = ConfigScalar.Boolean( false ),
                        ["keep"] = ConfigScalar.Integer( 1 ),
                    }
                ),
            }
        );
        var incoming = new ConfigObject(
            new Dictionary<string, ConfigValue>
            {
                ["nested"] = new ConfigObject(
                    new Dictionary<string, ConfigValue> { ["enabled"] = ConfigScalar.Boolean( true ) }
                ),
                ["count"] = ConfigScalar.Integer( 2 ),
            }
        );

        var merged = ConfigValueUtilities
            .Merge( current, incoming )
            .Should()
            .BeOfType<ConfigObject>()
            .Subject;

        merged["name"].Should().BeEquivalentTo( ConfigScalar.String( "old" ) );
        merged["count"].Should().BeEquivalentTo( ConfigScalar.Integer( 2 ) );
        merged["nested"]
            .Should()
            .BeEquivalentTo(
                new ConfigObject(
                    new Dictionary<string, ConfigValue>
                    {
                        ["enabled"] = ConfigScalar.Boolean( true ),
                        ["keep"] = ConfigScalar.Integer( 1 ),
                    }
                )
            );
    }

    [Fact]
    public void ResolveValue_resolves_references_templates_and_arrays()
    {
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue>
                {
                    ["name"] = ConfigScalar.String( "Alice" ),
                    ["alias"] = new VariableReference( "name" ),
                }
            )
        );
        var templateEngine = new TestTemplateEngine();
        var value = new ConfigObject(
            new Dictionary<string, ConfigValue>
            {
                ["resolved"] = new VariableReference( "alias" ),
                ["message"] = new TemplateMarkup( "Hello {{ name }}" ),
                ["items"] = new ConfigArray(
                [
                    ConfigScalar.Integer( 1 ),
                    new VariableReference( "alias" ),
                ] ),
            }
        );

        var resolved = ConfigValueUtilities
            .ResolveValue( value, variables, templateEngine )
            .Should()
            .BeOfType<ConfigObject>()
            .Subject;

        resolved["resolved"].Should().Be( ConfigScalar.String( "Alice" ) );
        resolved["message"].Should().Be( ConfigScalar.String( "Hello Alice" ) );
        var items = resolved["items"].Should().BeOfType<ConfigArray>().Subject;
        items.Should().HaveCount( 2 );
        items[0].Should().Be( ConfigScalar.Integer( 1 ) );
        items[1].Should().Be( ConfigScalar.String( "Alice" ) );
    }

    [Fact]
    public void ResolveValue_throws_for_reference_cycle()
    {
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue>
                {
                    ["a"] = new VariableReference( "b" ),
                    ["b"] = new VariableReference( "a" ),
                }
            )
        );

        var action = () => ConfigValueUtilities.ResolveValue( new VariableReference( "a" ), variables );

        action
            .Should()
            .Throw<ConfigurationException>()
            .WithMessage( "*Variable reference cycle detected*" );
    }

    [Fact]
    public void ToObjectGraph_and_ToJsonNode_convert_values()
    {
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue>
                {
                    ["name"] = ConfigScalar.String( "Alice" ),
                    ["count"] = ConfigScalar.Integer( 2 ),
                }
            )
        );
        var value = new ConfigObject(
            new Dictionary<string, ConfigValue>
            {
                ["name"] = new VariableReference( "name" ),
                ["count"] = new VariableReference( "count" ),
                ["items"] = new ConfigArray( [ConfigScalar.Boolean( true ), ConfigNull.Instance] ),
            }
        );

        var graph = ConfigValueUtilities
            .ToObjectGraph( value, variables )
            .Should()
            .BeAssignableTo<IDictionary<string, object?>>()
            .Subject;
        var json = ConfigValueUtilities
            .ToJsonNode( value, variables )
            .Should()
            .BeOfType<JsonObject>()
            .Subject;

        graph["name"].Should().Be( "Alice" );
        graph["count"].Should().Be( 2L );
        json["items"]![0]!.GetValue<bool>().Should().BeTrue();
        json["items"]![1].Should().BeNull();
    }

    [Fact]
    public void EnumeratePaths_flattens_leaf_values()
    {
        var value = new ConfigObject(
            new Dictionary<string, ConfigValue>
            {
                ["user"] = new ConfigObject(
                    new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String( "Alice" ) }
                ),
                ["count"] = ConfigScalar.Integer( 2 ),
            }
        );

        ConfigValueUtilities
            .EnumeratePaths( value )
            .Should()
            .ContainEquivalentOf( ("user.name", (ConfigValue)ConfigScalar.String( "Alice" )) )
            .And.ContainEquivalentOf( ("count", (ConfigValue)ConfigScalar.Integer( 2 )) );
    }
}