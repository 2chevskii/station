using FluentAssertions;

using Station.Abstractions.Execution;
using Station.Abstractions.FileSystem;
using Station.Abstractions.Modules;
using Station.Modules.Builtin.Apt;
using Station.Modules.Builtin.Scoop;
using Station.Modules.Builtin.Winget;
using Station.Testing;
using Station.Utilities.Processes;

namespace Station.IntegrationTests.Modules;

[Collection( "Environment variables" )]
public sealed class PackageManagerModuleTests
{
    [Fact]
    public async Task Scoop_module_uses_shimmed_command_without_touching_host_installation()
    {
        using var shims = new CommandShimScope( includeHostPath: false );
        var statePath = (shims.Path.AsAbsolutePath() / "scoop-state.txt").Value;
        var commandPath = shims.AddPowerShellCommand(
            "scoop",
            $$"""
              $state = '{{statePath.Replace( "'", "''" )}}'
              switch ($args[0]) {
                'export' {
                  $apps = @()
                  if (Test-Path -LiteralPath $state) {
                    $apps += @{ Name = Get-Content -LiteralPath $state; Info = 'Global install' }
                  }
                  @{ apps = $apps } | ConvertTo-Json -Compress
                  exit 0
                }
                'install' { Set-Content -LiteralPath $state -Value $args[-1]; exit 0 }
                'uninstall' { Remove-Item -LiteralPath $state -ErrorAction SilentlyContinue; exit 0 }
              }
              throw 'Unexpected Scoop fixture command.'
              """
        );

        var module = new ScoopModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: ExecutionMode.Apply
            ),
            new ProcessRunner()
        )
        {
            PackageName = "git",
            IsGlobal = true,
            Command = commandPath,
        };

        (await module.InspectAsync( CancellationToken.None ))
            .RequiredPrivilege.Should()
            .Be( PrivilegeRequirement.Administrator );
        await AssertChangeAsync( module, true );
        await AssertChangeAsync( module, false );
        module.State = "absent";
        await AssertChangeAsync( module, true );
        await AssertChangeAsync( module, false );
        File.Exists( statePath ).Should().BeFalse();
    }

    [Fact]
    public async Task Winget_module_uses_shimmed_command_without_touching_host_installation()
    {
        if ( !OperatingSystem.IsWindows() )
            Assert.Skip( "WinGet uses Windows process exit codes and is only supported on Windows." );

        using var shims = new CommandShimScope( includeHostPath: false );
        var statePath = (shims.Path.AsAbsolutePath() / "winget-state.txt").Value;
        var commandPath = shims.AddPowerShellCommand(
            "winget",
            $$"""
              $state = '{{statePath.Replace( "'", "''" )}}'
              switch ($args[0]) {
                'list' {
                  if (!(Test-Path -LiteralPath $state)) { exit -1978335212 }
                  Write-Output 'Name Id Version'
                  Write-Output '-----------------'
                  Write-Output ('Git Git.Git ' + (Get-Content -LiteralPath $state))
                  exit 0
                }
                'install' { Set-Content -LiteralPath $state -Value '2.0.0'; exit 0 }
                'uninstall' { Remove-Item -LiteralPath $state -ErrorAction SilentlyContinue; exit 0 }
              }
              throw 'Unexpected WinGet fixture command.'
              """
        );

        var module = new WingetModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: ExecutionMode.Apply
            ),
            new ProcessRunner()
        )
        {
            PackageId = "Git.Git",
            Version = "2.0.0",
            Scope = "machine",
            Command = commandPath,
        };

        (await module.InspectAsync( CancellationToken.None ))
            .RequiredPrivilege.Should()
            .Be( PrivilegeRequirement.Administrator );
        await AssertChangeAsync( module, true );
        await AssertChangeAsync( module, false );
        module.State = "absent";
        await AssertChangeAsync( module, true );
        await AssertChangeAsync( module, false );
        File.Exists( statePath ).Should().BeFalse();
    }

    [Fact]
    public async Task Apt_module_uses_isolated_commands_without_touching_host_installation()
    {
        using var shims = new CommandShimScope( includeHostPath: false );
        var statePath = (shims.Path.AsAbsolutePath() / "apt-state.txt").Value;
        var queryBody = $$"""
                          $state = '{{statePath.Replace( "'", "''" )}}'
                          if (Test-Path -LiteralPath $state) {
                            Write-Output ("installed`t" + (Get-Content -LiteralPath $state))
                            exit 0
                          }
                          exit 1
                          """;
        var updateBody = $$"""
                           $state = '{{statePath.Replace( "'", "''" )}}'
                           if ($args -contains 'install') {
                             Set-Content -LiteralPath $state -Value '1.0.0'
                             exit 0
                           }
                           if ($args -contains 'remove') {
                             Remove-Item -LiteralPath $state -ErrorAction SilentlyContinue
                             exit 0
                           }
                           throw 'Unexpected apt fixture command.'
                           """;
        var aptCommand = shims.AddPowerShellCommand( "apt-get", updateBody );
        shims.AddPowerShellCommand( "dpkg-query", queryBody );
        string? wslCommand = null;
        if ( OperatingSystem.IsWindows() )
        {
            wslCommand = shims.AddPowerShellCommand(
                "wsl",
                $$"""
                  if ($args[0] -eq '-l') {
                    [Console]::OutputEncoding = [Text.Encoding]::Unicode
                    Write-Output 'Ubuntu'
                    exit 0
                  }
                  if ($args -notcontains '--exec') { throw 'Expected direct WSL execution.' }
                  if ($args -contains 'dpkg-query') {
                    {{queryBody}}
                  }
                  {{updateBody}}
                  """
            );
        }
        else
        {
            shims.AddCommand( "sudo", "exec \"$@\"" );
        }

        var module = new AptModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: ExecutionMode.Apply
            ),
            new ProcessRunner()
        )
        {
            PackageName = "git",
            Distribution = "Ubuntu",
            Version = "1.0.0",
            WslCommand = wslCommand ?? "wsl-fixture-not-used-on-linux",
            AptGetCommand = aptCommand,
        };

        (await module.InspectAsync( CancellationToken.None )).IsValid.Should().BeTrue();
        await AssertChangeAsync( module, true );
        await AssertChangeAsync( module, false );
        module.State = "absent";
        await AssertChangeAsync( module, true );
        await AssertChangeAsync( module, false );
        File.Exists( statePath ).Should().BeFalse();
    }

    private static async Task AssertChangeAsync( ConfigModule module, bool expectedChange )
    {
        var result = await module.ExecuteAsync( CancellationToken.None );
        result.Failed.Should().BeFalse( result.Message );
        result.Changed.Should().Be( expectedChange );
    }
}
