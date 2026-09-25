using System.Text.Json.Nodes;
using FluentAssertions;
using Station.Abstractions.Configuration;
using Station.Abstractions.Modules;
using Station.Abstractions.Artifacts;
using Station.Core.Configuration.Values;
using Station.Testing;

namespace Station.UnitTests.Core;

public sealed class JsonNodeHelpersTests
{
    [Fact]
    public void ToJsonNode_supports_scalars_collections_and_dictionaries()
    {
        var node = JsonNodeHelpers
            .ToJsonNode(
                new Dictionary<string, object?>
                {
                    ["name"] = "Alice",
                    ["count"] = 2,
                    ["items"] = new object?[] { true, 3L },
                }
            )
            .Should()
            .BeOfType<JsonObject>()
            .Subject;

        node["name"]!.GetValue<string>().Should().Be("Alice");
        node["count"]!.GetValue<int>().Should().Be(2);
        node["items"]![1]!.GetValue<long>().Should().Be(3L);
    }

    [Fact]
    public void RenderNode_renders_only_string_scalars()
    {
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String("Alice") }
            )
        );
        var templateEngine = new TestTemplateEngine();
        var node = new JsonObject
        {
            ["name"] = "Hello {{ name }}",
            ["count"] = 2,
            ["items"] = new JsonArray("{{ name }}", 3),
        };

        var rendered = JsonNodeHelpers.RenderStringScalars(node, variables, templateEngine);

        rendered["name"]!.GetValue<string>().Should().Be("Hello Alice");
        rendered["count"]!.GetValue<int>().Should().Be(2);
        rendered["items"]![0]!.GetValue<string>().Should().Be("Alice");
    }

    [Fact]
    public void NormalizeYamlValue_normalizes_dates_mappings_and_lists()
    {
        var value = JsonNodeHelpers
            .NormalizeYamlValue(
                new Dictionary<object, object>
                {
                    [1] = new DateTime(2026, 4, 12, 1, 2, 3, DateTimeKind.Utc),
                    ["items"] = new object?[] { "a", 2L },
                }
            )
            .Should()
            .BeAssignableTo<IDictionary<string, object?>>()
            .Subject;

        value.Should().ContainKey("1");
        value["items"].Should().BeAssignableTo<IReadOnlyList<object?>>();
    }

    [Fact]
    public void GetObject_returns_property_or_throws()
    {
        var json = new JsonObject { ["nested"] = new JsonObject { ["value"] = 1 } };

        json.GetObject("nested")["value"]!.GetValue<int>().Should().Be(1);
        var action = () => json.GetObject("missing");
        action
            .Should()
            .Throw<ConfigurationException>()
            .WithMessage("Parameter 'missing' must be an object.");
    }
}
