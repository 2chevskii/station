using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

using FluentAssertions;

using Station.Abstractions.Artifacts;
using Station.Abstractions.Configuration;
using Station.Abstractions.Execution;
using Station.Abstractions.Modules;
using Station.Modules.Builtin.Dotfile;
using Station.Testing;

namespace Station.IntegrationTests.Modules;

public sealed class FileModuleWriteTests
{
    private static readonly DateTimeOffset Timestamp =
        new DateTimeOffset( 2026, 4, 12, 9, 10, 11, TimeSpan.Zero );

    [Theory]
    [InlineData( false, false )]
    [InlineData( false, true )]
    [InlineData( true, false )]
    [InlineData( true, true )]
    public async Task Symbolic_link_destinations_keep_the_link_and_update_its_target(
        bool template,
        bool targetExists
    )
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory( directory.GetPath( "out" ) );
        if ( targetExists )
            directory.WriteFile( "physical/config.txt", "original" );

        var linkPath = directory.GetPath( "out/config.txt" );
        var targetPath = directory.GetPath( "physical/config.txt" );
        var relativeTarget = Path.GetRelativePath( directory.GetPath( "out" ), targetPath );
        File.CreateSymbolicLink( linkPath, relativeTarget );
        try
        {
            var module = CreateModule( directory, template, "replacement" );
            (await module.InspectAsync( CancellationToken.None )).IsValid.Should().BeTrue();

            var result = await module.ExecuteAsync( CancellationToken.None );

            result.Failed.Should().BeFalse( result.Message );
            File.ReadAllText( targetPath ).Should().Be( "replacement" );
            new FileInfo( linkPath ).LinkTarget.Should().Be( relativeTarget );
            File.Exists( linkPath + ".bak.20260412091011" ).Should().Be( targetExists );
            if ( targetExists )
                File.ReadAllText( linkPath + ".bak.20260412091011" ).Should().Be( "original" );

            Directory.GetFiles( directory.GetPath( "physical" ) )
                .Should().Equal( targetPath );
        }
        finally
        {
            File.Delete( linkPath );
        }
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Existing_access_permissions_are_preserved( bool template )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "out/config.txt", "original" );
        var path = directory.GetPath( "out/config.txt" );
        string[]? accessRules = null;
        var unixPermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        if ( OperatingSystem.IsWindows() )
        {
            var file = new FileInfo( path );
            var security = file.GetAccessControl( AccessControlSections.Access );
            security.SetAccessRuleProtection( true, preserveInheritance: true );
            file.SetAccessControl( security );
            accessRules = GetAccessRules( file.GetAccessControl( AccessControlSections.Access ) );
        }
        else
        {
            File.SetUnixFileMode( path, unixPermissions );
        }

        var result = await CreateModule( directory, template, "replacement" )
            .ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        if ( OperatingSystem.IsWindows() )
        {
            var security = new FileInfo( path ).GetAccessControl( AccessControlSections.Access );
            security.AreAccessRulesProtected.Should().BeTrue();
            security.AreAccessRulesCanonical.Should().BeTrue();
            GetAccessRules( security ).Should().BeEquivalentTo( accessRules );
            var backupSecurity = new FileInfo( path + ".bak.20260412091011" )
                .GetAccessControl( AccessControlSections.Access );
            backupSecurity.AreAccessRulesProtected.Should().BeTrue();
            backupSecurity.AreAccessRulesCanonical.Should().BeTrue();
            GetAccessRules( backupSecurity ).Should().BeEquivalentTo( accessRules );
        }
        else
        {
            File.GetUnixFileMode( path ).Should().Be( unixPermissions );
            File.GetUnixFileMode( path + ".bak.20260412091011" ).Should().Be( unixPermissions );
        }
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Read_only_destination_fails_without_clearing_its_attributes( bool template )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "out/config.txt", "original" );
        var path = directory.GetPath( "out/config.txt" );
        File.SetAttributes( path, File.GetAttributes( path ) | FileAttributes.ReadOnly );
        try
        {
            var result = await CreateModule( directory, template, "replacement" )
                .ExecuteAsync( CancellationToken.None );

            result.Failed.Should().BeTrue();
            File.ReadAllText( path ).Should().Be( "original" );
            File.GetAttributes( path ).Should().HaveFlag( FileAttributes.ReadOnly );
            Directory.GetFiles( directory.GetPath( "out" ) ).Should().Equal( path );
        }
        finally
        {
            File.SetAttributes( path, FileAttributes.Normal );
        }
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Repeated_writes_in_one_second_preserve_every_previous_version( bool template )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "out/config.txt", "original" );
        var clock = new FakeTimeProvider( Timestamp );

        var first = await CreateModule( directory, template, "first", clock: clock )
            .ExecuteAsync( CancellationToken.None );
        var second = await CreateModule( directory, template, "second", clock: clock )
            .ExecuteAsync( CancellationToken.None );

        first.Changed.Should().BeTrue();
        second.Changed.Should().BeTrue();
        File.ReadAllText( directory.GetPath( "out/config.txt" ) ).Should().Be( "second" );
        File.ReadAllText( directory.GetPath( "out/config.txt.bak.20260412091011" ) )
            .Should().Be( "original" );
        File.ReadAllText( directory.GetPath( "out/config.txt.bak.20260412091011.1" ) )
            .Should().Be( "first" );
    }

    [Theory]
    [InlineData( false, true )]
    [InlineData( false, false )]
    [InlineData( true, true )]
    [InlineData( true, false )]
    public async Task Replacement_keeps_an_open_reader_on_the_complete_previous_file(
        bool template,
        bool backup
    )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "out/config.txt", "complete previous contents" );
        await using var previousFile = new FileStream(
            directory.GetPath( "out/config.txt" ),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );

        var result = await CreateModule( directory, template, "new contents", backup: backup )
            .ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().BeTrue();
        File.ReadAllText( directory.GetPath( "out/config.txt" ) ).Should().Be( "new contents" );
        using var reader = new StreamReader( previousFile, Encoding.UTF8, leaveOpen: true );
        (await reader.ReadToEndAsync()).Should().Be( "complete previous contents" );
        Directory.GetFiles( directory.GetPath( "out" ), "*.tmp" ).Should().BeEmpty();
        Directory.GetFiles( directory.GetPath( "out" ), "*.bak.*" )
            .Should().HaveCount( backup ? 1 : 0 );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Locked_destination_fails_without_changing_data_or_leaving_staging_files(
        bool template
    )
    {
        if ( !OperatingSystem.IsWindows() )
            Assert.Skip( "Windows delete-sharing semantics are required." );

        using var directory = new TemporaryDirectory();
        directory.WriteFile( "out/config.txt", "original" );
        await using var lockedFile = new FileStream(
            directory.GetPath( "out/config.txt" ),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );

        var result = await CreateModule( directory, template, "replacement" )
            .ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeTrue();
        File.ReadAllText( directory.GetPath( "out/config.txt" ) ).Should().Be( "original" );
        Directory.GetFiles( directory.GetPath( "out" ) )
            .Should().Equal( directory.GetPath( "out/config.txt" ) );
    }

    [Theory]
    [InlineData( false, ExecutionMode.Apply )]
    [InlineData( true, ExecutionMode.Apply )]
    [InlineData( false, ExecutionMode.Check )]
    [InlineData( true, ExecutionMode.Check )]
    public async Task Pre_cancelled_execution_does_not_create_the_destination_directory(
        bool template,
        ExecutionMode mode
    )
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var module = CreateModule( directory, template, "replacement", mode );

        var action = () => module.ExecuteAsync( cancellation.Token ).AsTask();

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists( directory.GetPath( "out" ) ).Should().BeFalse();
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Cancellation_before_commit_keeps_previous_contents_and_removes_staging_files(
        bool template
    )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "out/config.txt", "original" );
        using var cancellation = new CancellationTokenSource();
        var clock = new CallbackTimeProvider( cancellation.Cancel );
        var module = CreateModule( directory, template, "replacement", clock: clock );

        var action = () => module.ExecuteAsync( cancellation.Token ).AsTask();

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText( directory.GetPath( "out/config.txt" ) ).Should().Be( "original" );
        Directory.GetFiles( directory.GetPath( "out" ) )
            .Should().Equal( directory.GetPath( "out/config.txt" ) );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Existing_backup_files_and_directories_are_never_overwritten( bool template )
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile( "out/config.txt", "original" );
        directory.WriteFile( "out/config.txt.bak.20260412091011", "older backup" );
        Directory.CreateDirectory( directory.GetPath( "out/config.txt.bak.20260412091011.1" ) );
        var module = CreateModule(
            directory, template, "replacement", clock: new FakeTimeProvider( Timestamp )
        );

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Failed.Should().BeFalse( result.Message );
        File.ReadAllText( directory.GetPath( "out/config.txt.bak.20260412091011" ) )
            .Should().Be( "older backup" );
        Directory.Exists( directory.GetPath( "out/config.txt.bak.20260412091011.1" ) )
            .Should().BeTrue();
        File.ReadAllText( directory.GetPath( "out/config.txt.bak.20260412091011.2" ) )
            .Should().Be( "original" );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Check_does_not_create_directories_or_backups( bool template )
    {
        using var directory = new TemporaryDirectory();
        var module = CreateModule( directory, template, "replacement", ExecutionMode.Check );

        var result = await module.ExecuteAsync( CancellationToken.None );

        result.Changed.Should().BeTrue();
        Directory.Exists( directory.GetPath( "out" ) ).Should().BeFalse();
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Empty_destination_is_rejected_during_inspection( bool template )
    {
        using var directory = new TemporaryDirectory();
        var module = CreateModule( directory, template, "replacement", destination: "" );

        var inspection = await module.InspectAsync( CancellationToken.None );
        var result = await module.ExecuteAsync( CancellationToken.None );

        inspection.IsValid.Should().BeFalse();
        result.Failed.Should().BeTrue();
        result.Message.Should().Contain( "destination" );
    }

    private static ConfigModule CreateModule(
        TemporaryDirectory directory,
        bool template,
        string content,
        ExecutionMode mode = ExecutionMode.Apply,
        bool backup = true,
        TimeProvider? clock = null,
        string? destination = null
    )
    {
        directory.WriteFile( "inputs/sample.txt", content );
        var variables = new VariableSet( new ConfigObject() );
        var templateEngine = new TestTemplateEngine();
        var managedTemplate = new ManagedTemplate(
            "sample", directory.GetPath( "inputs/sample.txt" ), variables, templateEngine
        );
        var artifacts = new ModuleArtifactSet(
            new Dictionary<string, ManagedFile>
            {
                ["sample"] = new ManagedFile( "sample", directory.GetPath( "inputs/sample.txt" ) ),
            },
            new Dictionary<string, ManagedTemplate> { ["sample"] = managedTemplate },
            new Dictionary<string, IReadOnlyList<ManagedTemplate>> { ["sample"] = [managedTemplate] }
        );
        var context = TestModuleContextFactory.Create(
            directory.Path,
            directory.Path,
            variables,
            artifacts,
            templateEngine,
            mode,
            timeProvider: clock ?? new FakeTimeProvider( Timestamp )
        );
        var output = destination ?? directory.GetPath( "out/config.txt" );
        return template
            ? new TemplateModule( context )
            {
                TemplateName = "sample",
                Destination = output,
                Backup = backup,
            }
            : new CopyModule( context )
            {
                FileName = "sample",
                Destination = output,
                Backup = backup,
            };
    }

    private sealed class CallbackTimeProvider( Action callback ) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            callback();
            return Timestamp;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform( "windows" )]
    private static string[] GetAccessRules( FileSecurity security ) =>
        security.GetAccessRules( true, true, typeof( SecurityIdentifier ) )
            .Cast<FileSystemAccessRule>()
            .Select( rule =>
                $"{rule.IdentityReference.Value}|{rule.FileSystemRights}|{rule.AccessControlType}"
                + $"|{rule.IsInherited}|{rule.InheritanceFlags}|{rule.PropagationFlags}"
            )
            .ToArray();
}
