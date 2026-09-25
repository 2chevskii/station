using FluentAssertions;
using Station.Abstractions.Enums;
using Station.Modules.Builtin.Apt;
using Station.Modules.Builtin.Scoop;
using Station.Modules.Builtin.Winget;
using Station.Testing;
using TruePath;

namespace Station.IntegrationTests.Modules;

public sealed class PackageManagerModuleTests
{
    [Fact]
    public async Task Scoop_module_uses_shimmed_command_without_touching_host_installation()
    {
        using var shims = new CommandShimScope();
        var statePath = (new AbsolutePath(shims.Path) / "scoop-state.txt").Value;
        var commandPath = shims.AddPowerShellCommand(
            "scoop",
            $$"""
            $state = '{{statePath.Replace("'", "''")}}'
            switch ($args[0]) {
              'list' { if (Test-Path $state) { Get-Content $state }; exit 0 }
              'install' { Set-Content -Path $state -Value $args[-1]; exit 0 }
              'uninstall' { Remove-Item -Path $state -ErrorAction SilentlyContinue; exit 0 }
            }
            """
        );

        var module = new ScoopModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: ExecutionMode.Apply
            )
        )
        {
            PackageName = "git",
            IsGlobal = true,
            Command = commandPath,
        };

        (await module.InspectAsync(CancellationToken.None))
            .RequiredPrivilege.Should()
            .Be(PrivilegeRequirement.Administrator);
        (await module.ExecuteAsync(CancellationToken.None)).Changed.Should().BeTrue();
        (await module.ExecuteAsync(CancellationToken.None)).Changed.Should().BeFalse();
    }

    [Fact]
    public async Task Winget_module_uses_shimmed_command_without_touching_host_installation()
    {
        using var shims = new CommandShimScope();
        var statePath = (new AbsolutePath(shims.Path) / "winget-state.txt").Value;
        var commandPath = shims.AddPowerShellCommand(
            "winget",
            $$"""
            $state = '{{statePath.Replace("'", "''")}}'
            switch ($args[0]) {
              'list' { if (Test-Path $state) { Write-Output ('Git.Git ' + (Get-Content $state)) }; exit 0 }
              'install' { Set-Content -Path $state -Value '2.0.0'; exit 0 }
              'uninstall' { Remove-Item -Path $state -ErrorAction SilentlyContinue; exit 0 }
            }
            """
        );

        var module = new WingetModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: ExecutionMode.Apply
            )
        )
        {
            PackageId = "Git.Git",
            Version = "2.0.0",
            Scope = "machine",
            Command = commandPath,
        };

        (await module.InspectAsync(CancellationToken.None))
            .RequiredPrivilege.Should()
            .Be(PrivilegeRequirement.Administrator);
        (await module.ExecuteAsync(CancellationToken.None)).Changed.Should().BeTrue();
        (await module.ExecuteAsync(CancellationToken.None)).Changed.Should().BeFalse();
    }

    [Fact]
    public async Task Apt_module_uses_shimmed_wsl_without_touching_host_installation()
    {
        using var shims = new CommandShimScope();
        var statePath = (new AbsolutePath(shims.Path) / "apt-state.txt").Value;
        var commandPath = shims.AddPowerShellCommand(
            "wsl",
            $$"""
            $state = '{{statePath.Replace("'", "''")}}'
            if ($args[0] -eq '-l') { Write-Output 'Ubuntu'; exit 0 }
            if ($args -contains 'dpkg-query') { if (Test-Path $state) { Get-Content $state; exit 0 } exit 1 }
            if ($args -contains 'install') { Set-Content -Path $state -Value '1.0.0'; exit 0 }
            if ($args -contains 'remove') { Remove-Item -Path $state -ErrorAction SilentlyContinue; exit 0 }
            """
        );

        var module = new AptModule(
            TestModuleContextFactory.Create(
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                mode: ExecutionMode.Apply
            )
        )
        {
            PackageName = "git",
            Distribution = "Ubuntu",
            Version = "1.0.0",
            WslCommand = commandPath,
        };

        (await module.InspectAsync(CancellationToken.None)).IsValid.Should().BeTrue();
        (await module.ExecuteAsync(CancellationToken.None)).Changed.Should().BeTrue();
        (await module.ExecuteAsync(CancellationToken.None)).Changed.Should().BeFalse();
    }
}
