using FluentAssertions;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Models;
using Station.Testing;
using TruePath;
using TruePath.SystemIo;

namespace Station.UnitTests.Abstractions;

public sealed class ModuleArtifactsTests
{
    [Fact]
    public void ManagedFile_reads_bytes_and_text()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.GetPath("files", "sample.txt");
        path.AsAbsolutePath().Parent!.Value.CreateDirectory();
        path.AsAbsolutePath().WriteAllText("hello");
        var file = new ManagedFile("sample.txt", path);

        file.ReadAllText().Should().Be("hello");
        file.ReadAllBytes().Should().BeEquivalentTo("hello"u8.ToArray());
    }

    [Fact]
    public void ManagedTemplate_caches_raw_and_rendered_text_and_supports_overlay_render()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.GetPath("templates", "sample.scriban");
        path.AsAbsolutePath().Parent!.Value.CreateDirectory();
        path.AsAbsolutePath().WriteAllText("hello {{ name }}");
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String("Alice") }
            )
        );
        var engine = new TestTemplateEngine();
        var template = new ManagedTemplate("sample.scriban", path, variables, engine);

        template.GetRawText().Should().Be("hello {{ name }}");
        template.GetRenderedText().Should().Be("hello Alice");
        template.GetRenderedText().Should().Be("hello Alice");
        template
            .Render(
                new VariableSet(
                    new ConfigObject(
                        new Dictionary<string, ConfigValue>
                        {
                            ["name"] = ConfigScalar.String("Bob"),
                        }
                    )
                )
            )
            .Should()
            .Be("hello Bob");

        engine.Calls.Should().HaveCount(2);
    }

    [Fact]
    public void ModuleArtifactSet_resolves_files_and_templates_by_normalized_name()
    {
        using var directory = new TemporaryDirectory();
        var templatePath = directory.GetPath("templates", "app.scriban");
        templatePath.AsAbsolutePath().Parent!.Value.CreateDirectory();
        templatePath.AsAbsolutePath().WriteAllText("hello");
        var template = new ManagedTemplate(
            "nested/app.scriban",
            templatePath,
            new VariableSet(new ConfigObject()),
            new TestTemplateEngine()
        );
        var artifacts = new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase)
            {
                ["nested/file.txt"] = new("nested/file.txt", templatePath),
            },
            new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase)
            {
                ["nested/app.scriban"] = template,
            },
            new Dictionary<string, IReadOnlyList<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["nested/app"] = [template],
            }
        );

        artifacts.GetRequiredFile("\\nested\\file.txt").Name.Should().Be("nested/file.txt");
        artifacts.GetRequiredTemplate("/nested/app").Name.Should().Be("nested/app.scriban");
    }

    [Fact]
    public void ModuleArtifactSet_reports_ambiguous_template_names()
    {
        var templateA = new ManagedTemplate(
            "app.scriban",
            Temporary.CreateTempFile().Value,
            new VariableSet(new ConfigObject()),
            new TestTemplateEngine()
        );
        var templateB = new ManagedTemplate(
            "app.sbn",
            Temporary.CreateTempFile().Value,
            new VariableSet(new ConfigObject()),
            new TestTemplateEngine()
        );
        var artifacts = new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase)
            {
                ["app.scriban"] = templateA,
                ["app.sbn"] = templateB,
            },
            new Dictionary<string, IReadOnlyList<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["app"] = [templateA, templateB],
            }
        );

        var success = artifacts.TryGetTemplate("app", out _, out var error);

        success.Should().BeFalse();
        error.Should().Contain("ambiguous");
    }
}
