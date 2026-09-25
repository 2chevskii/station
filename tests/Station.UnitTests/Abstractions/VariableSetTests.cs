using FluentAssertions;

using Station.Abstractions.Configuration;
using Station.Testing;

namespace Station.UnitTests.Abstractions;

public sealed class VariableSetTests
{
    [Fact]
    public void CreateOverlay_merges_overlay_values()
    {
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue>
                {
                    ["name"] = ConfigScalar.String( "Alice" ),
                    ["nested"] = new ConfigObject(
                        new Dictionary<string, ConfigValue>
                        {
                            ["enabled"] = ConfigScalar.Boolean( false ),
                        }
                    ),
                }
            )
        );

        var overlay = variables.CreateOverlay(
            new ConfigObject(
                new Dictionary<string, ConfigValue>
                {
                    ["nested"] = new ConfigObject(
                        new Dictionary<string, ConfigValue>
                        {
                            ["enabled"] = ConfigScalar.Boolean( true ),
                        }
                    ),
                }
            )
        );

        overlay.Get<bool>( "nested.enabled" ).Should().BeTrue();
        overlay.Get<string>( "name" ).Should().Be( "Alice" );
    }

    [Fact]
    public void Get_and_TryGetValue_resolve_reference_values()
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

        variables.Contains( "alias" ).Should().BeTrue();
        variables.Get<string>( "alias" ).Should().Be( "Alice" );
        variables.TryGetValue<string>( "alias", out var alias ).Should().BeTrue();
        alias.Should().Be( "Alice" );
    }

    [Fact]
    public void TryGetValue_returns_false_for_unconvertible_value()
    {
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String( "Alice" ) }
            )
        );

        variables.TryGetValue<int>( "name", out var value ).Should().BeFalse();
        value.Should().Be( 0 );
    }

    [Fact]
    public void TryGetRaw_returns_false_for_missing_path()
    {
        var variables = new VariableSet( new ConfigObject() );

        variables.TryGetRaw( "missing.path", out var value ).Should().BeFalse();
        value.Should().BeSameAs( ConfigNull.Instance );
    }

    [Fact]
    public void ToObjectGraph_and_EnumeratePaths_return_visible_values()
    {
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue>
                {
                    ["user"] = new ConfigObject(
                        new Dictionary<string, ConfigValue>
                        {
                            ["name"] = ConfigScalar.String( "Alice" ),
                        }
                    ),
                }
            )
        );
        var templateEngine = new TestTemplateEngine();

        variables
            .ToObjectGraph( templateEngine )
            .Should()
            .BeAssignableTo<IDictionary<string, object?>>();
        variables.EnumeratePaths().Should().ContainSingle( x => x.Path == "user.name" );
    }
}