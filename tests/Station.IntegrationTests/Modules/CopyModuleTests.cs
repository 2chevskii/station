using FluentAssertions;
using Station.Abstractions.Execution;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Artifacts;
using Station.Modules.Builtin.Dotfile;
using Station.Testing;

using TruePath.SystemIo;

namespace Station.IntegrationTests.Modules;

public sealed class CopyModuleTests
{
    [Fact]
    public async Task Execute_copies_file_and_creates_backup_when_enabled()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = directory.GetPath("role", "files", "sample.txt");
        sourcePath.AsAbsolutePath().Parent!.Value.CreateDirectory();
        await sourcePath.AsAbsolutePath().WriteAllTextAsync("hello");
        var artifacts = new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase)
            {
                ["sample.txt"] = new("sample.txt", sourcePath),
            },
            new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase)
        );
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 4, 12, 9, 10, 11, TimeSpan.Zero)
        );
        var context = TestModuleContextFactory.Create(
            directory.Path,
            directory.GetPath("role"),
            artifacts: artifacts,
            mode: ExecutionMode.Apply,
            timeProvider: timeProvider
        );
        var destination = directory.GetPath("out.txt");
        await destination.AsAbsolutePath().WriteAllTextAsync("old");
        var module = new CopyModule(context)
        {
            FileName = "sample.txt",
            Destination = destination,
            Backup = true,
        };

        (await module.InspectAsync(CancellationToken.None)).IsValid.Should().BeTrue();
        var result = await module.ExecuteAsync(CancellationToken.None);

        result.Changed.Should().BeTrue();
        (await destination.AsAbsolutePath().ReadAllTextAsync()).Should().Be("hello");
        (destination + ".bak.20260412091011").AsAbsolutePath().ExistsFile().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_in_check_mode_is_idempotent()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = directory.GetPath("role", "files", "sample.txt");
        sourcePath.AsAbsolutePath().Parent!.Value.CreateDirectory();
        await sourcePath.AsAbsolutePath().WriteAllTextAsync("hello");
        var artifacts = new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase)
            {
                ["sample.txt"] = new("sample.txt", sourcePath),
            },
            new Dictionary<string, ManagedTemplate>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<ManagedTemplate>>(StringComparer.OrdinalIgnoreCase)
        );
        var destination = directory.GetPath("out.txt");
        await destination.AsAbsolutePath().WriteAllTextAsync("hello");
        var context = TestModuleContextFactory.Create(
            directory.Path,
            directory.GetPath("role"),
            artifacts: artifacts,
            mode: ExecutionMode.Check
        );
        var module = new CopyModule(context) { FileName = "sample.txt", Destination = destination };

        var result = await module.ExecuteAsync(CancellationToken.None);

        result.Changed.Should().BeFalse();
        result.Message.Should().Contain("already in the desired state");
    }

    [Fact]
    public async Task Inspect_and_execute_report_missing_file()
    {
        using var directory = new TemporaryDirectory();
        var module = new CopyModule(TestModuleContextFactory.Create(directory.Path, directory.Path))
        {
            FileName = "missing.txt",
            Destination = directory.GetPath("out.txt"),
        };

        var inspection = await module.InspectAsync(CancellationToken.None);
        var result = await module.ExecuteAsync(CancellationToken.None);

        inspection.IsValid.Should().BeFalse();
        result.Failed.Should().BeTrue();
    }
}
