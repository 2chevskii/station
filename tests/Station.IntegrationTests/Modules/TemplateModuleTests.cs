using FluentAssertions;
using Station.Abstractions.Enums;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Models;
using Station.Modules.Builtin.Dotfile;
using Station.Testing;
using TruePath;
using TruePath.SystemIo;

namespace Station.IntegrationTests.Modules;

public sealed class TemplateModuleTests
{
    [Fact]
    public async Task Execute_is_idempotent_for_template_module()
    {
        using var directory = new TemporaryDirectory();
        var templatePath = directory.GetPath("role", "templates", "app.scriban");
        templatePath.AsAbsolutePath().Parent!.Value.CreateDirectory();
        await templatePath.AsAbsolutePath().WriteAllTextAsync("hello {{ name }}");
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String("Alice") }
            )
        );
        var engine = new TestTemplateEngine();
        var artifacts = new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase)
            {
                ["app.scriban"] = new("app.scriban", templatePath, variables, engine),
            },
            new Dictionary<string, IReadOnlyList<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["app"] = [new ManagedTemplate("app.scriban", templatePath, variables, engine)],
            }
        );
        var destination = directory.GetPath("out.txt");

        var checkModule = new TemplateModule(
            TestModuleContextFactory.Create(
                directory.Path,
                directory.GetPath("role"),
                variables,
                artifacts,
                engine,
                ExecutionMode.Check
            )
        )
        {
            TemplateName = "app",
            Destination = destination,
        };
        var applyModule = new TemplateModule(
            TestModuleContextFactory.Create(
                directory.Path,
                directory.GetPath("role"),
                variables,
                artifacts,
                engine,
                ExecutionMode.Apply
            )
        )
        {
            TemplateName = "app",
            Destination = destination,
        };

        (await checkModule.ExecuteAsync(CancellationToken.None)).Changed.Should().BeTrue();
        (await applyModule.ExecuteAsync(CancellationToken.None)).Changed.Should().BeTrue();
        (await destination.AsAbsolutePath().ReadAllTextAsync()).Should().Be("hello Alice");
        (await checkModule.ExecuteAsync(CancellationToken.None)).Changed.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_creates_backup_when_content_changes()
    {
        using var directory = new TemporaryDirectory();
        var templatePath = directory.GetPath("role", "templates", "app.scriban");
        templatePath.AsAbsolutePath().Parent!.Value.CreateDirectory();
        await templatePath.AsAbsolutePath().WriteAllTextAsync("hello {{ name }}");
        var variables = new VariableSet(
            new ConfigObject(
                new Dictionary<string, ConfigValue> { ["name"] = ConfigScalar.String("Alice") }
            )
        );
        var engine = new TestTemplateEngine();
        var template = new ManagedTemplate("app.scriban", templatePath, variables, engine);
        var artifacts = new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase)
            {
                ["app.scriban"] = template,
            },
            new Dictionary<string, IReadOnlyList<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["app"] = [template],
            }
        );
        var destination = directory.GetPath("out.txt");
        await destination.AsAbsolutePath().WriteAllTextAsync("old");
        var module = new TemplateModule(
            TestModuleContextFactory.Create(
                directory.Path,
                directory.GetPath("role"),
                variables,
                artifacts,
                engine,
                ExecutionMode.Apply,
                timeProvider: new FakeTimeProvider(
                    new DateTimeOffset(2026, 4, 12, 9, 10, 11, TimeSpan.Zero)
                )
            )
        )
        {
            TemplateName = "app",
            Destination = destination,
            Backup = true,
        };

        var result = await module.ExecuteAsync(CancellationToken.None);

        result.Changed.Should().BeTrue();
        (destination + ".bak.20260412091011").AsAbsolutePath().ExistsFile().Should().BeTrue();
    }
}
