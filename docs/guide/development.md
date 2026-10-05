# Development

## Repository layout

Station targets .NET 10. Its projects separate contracts, configuration loading, execution, plugin restoration and built-in modules:

| Project | Responsibility |
| --- | --- |
| `Station.Abstractions` | Module, variable, artifact and process execution contracts. |
| `Station.Configuration.Abstractions` | Configuration document contracts and values. |
| `Station.Configuration.Yaml` | YAML loading and Scriban rendering. |
| `Station.Configuration.JavaScript` | Jint-based variable and role loading. |
| `Station.Core` | Discovery, binding, planning and task execution. |
| `Station.Plugins.Abstractions` | Plugin manifest and requirement contracts. |
| `Station.Plugins` | Provider-specific restore and caching. |
| `Station.Utilities` | Shared process and privilege utilities. |
| `Station.Modules.*` | Built-in modules. |
| `Station` | Command-line entry point and output. |

The core task model is Ansible-like: optional name, one module key and a nested parameter object. YAML is a serialization format; the in-memory .NET model is the source of truth.

## Dependency injection

`StationServices.AddStation` registers application dependencies. `Program.cs` creates the `IServiceCollection`, configures Spectre and runs the application. `TypeRegistrar.Build` creates one container with `ValidateOnBuild` and `ValidateScopes`; the registrar asynchronously disposes it after execution. Spectre registers command classes automatically. `AddStationCore` and `AddStationPlugins` register their respective internal services without creating a container.

Configuration loaders, plugin providers, the template engine, process runner, privilege detector, HTTP client and time provider are shared services. Modules are created for each task through `ActivatorUtilities`, with a fresh `ModuleContext` and any additional constructor dependencies resolved from DI. Integration tests use the CLI registrations and can replace individual dependencies before building the container.

`IProcessRunner`, `ProcessCommand` and `ProcessResult` live in `Station.Abstractions.Processes`. The implementation remains `Station.Utilities.Processes.ProcessRunner`. The `script`, `apt`, `scoop` and `winget` modules receive `IProcessRunner` through their constructors, keeping their project dependencies limited to Abstractions. Code that previously imported the process contracts from Utilities must update its namespace; code that directly constructs these modules must supply a runner.

## Execution and cancellation

`ConfigEngine` propagates requested cancellation as `OperationCanceledException`. It checks cancellation before configuration loading and between task stages. Modules should pass the token to asynchronous work and let cancellation propagate. A module that returns after cancellation cannot cause the next stage or task to start.

The shared process runner rejects an already cancelled request before launching a command. Cancellation of a running command requests termination of its process tree, waits for the parent process to exit and observes the redirected output reads. Script modules remove their temporary request files during cleanup. This is covered by integration tests using real PowerShell processes, including a parent and child process.

Process cleanup follows the platform's [`Process.Kill(entireProcessTree: true)` contract](https://learn.microsoft.com/dotnet/api/system.diagnostics.process.kill): descendant termination depends on OS permissions, and waiting for the parent does not itself prove that every descendant has exited. The integration test separately waits for its child process.

## Development direction

The next releases focus on dependable workstation changes and automation:

1. **Execution reliability — implemented.** Stop `check/apply` on task errors, preserve diagnostics and completed task records, propagate cancellation and share process execution across built-in modules and plugin restoration.
2. **Package and script contracts — implemented.** Package modules reject invalid states and query failures, inspect exact package identities and installation scopes, and validate installed versions. Script response types and duplicate properties are checked. Regression tests cover these contracts and controlled command fixtures verify idempotence. Read-only checks also cover the installed Scoop, WinGet and WSL/dpkg tools.
3. **Automation output — implemented.** Every command supports `--output json` with schema version 1, explicit task statuses, structured module outputs, diagnostics and exit codes. Progress and managed module console output go to stderr. Subprocess tests verify successful and failed results; application tests cover cancellation and output restoration.
4. **File update integrity — implemented.** `copy` and `template` stage complete contents and replace existing files, reserve backup names without overwriting previous versions, preserve access permissions and follow file symbolic links. Real filesystem tests cover open readers, Windows sharing failures, cancellation, backup collisions, dangling links and Unix modes on Windows and Linux.
5. **Cross-platform test isolation — implemented.** Command fixtures use native launchers on Windows and Linux. Package-manager tests isolate `PATH`, whitelist the .NET runtime when needed by PowerShell, and use controlled commands for installation, removal and queries. Both complete test suites run locally on Windows and Linux; CI has Windows and Ubuntu jobs with separate artifacts. Platform-specific checks report explicit skips.

File replacement uses [`File.Replace`](https://learn.microsoft.com/dotnet/api/system.io.file.replace) with a temporary recovery path on the same volume. If Windows reports `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2`, Station attempts to restore the original path and retains the recovery file if restoration also fails. That branch follows the [documented Windows error contract](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew); automated tests exercise ordinary sharing failures and cancellation rather than injecting this rare operating-system failure.

## CLI output architecture

`Program.cs` owns the startup sequence. `StationCommands.Configure` declares the command tree and the `list` branch. Commands under `Station/Commands` inherit from `Spectre.Console.Cli`'s `AsyncCommand<StationSettings>` through `StationCommand`. Shared options use `[CommandOption]`, `[Description]` and `[DefaultValue]` attributes in `StationSettings`.

`CommandOutputSession` registers output services, buffers help, formats errors and selects text or JSON output. `ConsoleRedirection` restores console writers after completion, failure or cancellation. `CommandArguments` preserves global option placement, multi-value `--set` syntax and rejection of repeated scalar options. Spectre handles parsing, help and validation. `StationConsole` and `JsonCommandOutput` implement `ICommandOutput` for text and JSON rendering.

The JSON writer projects engine results into an explicit versioned contract and buffers a complete document before writing it to stdout. It does not serialize plugin manifests or other engine internals. The CLI handles one invocation per process and temporarily routes managed console writes to stderr for JSON commands, restoring both writers on completion, failure or cancellation. The [CLI reference](./cli.md#json-results) defines the public format.

## Build and test

Tests require .NET 10, Git and PowerShell 7 (`pwsh`) on `PATH`. From the repository root:

```powershell
dotnet build Station.slnx -v minimal
dotnet test --project tests/Station.UnitTests/Station.UnitTests.csproj -v minimal
dotnet test --project tests/Station.IntegrationTests/Station.IntegrationTests.csproj -v minimal
```

Run build and test commands sequentially because the projects share output and restore state. The repository selects [Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/tools/dotnet-test-mtp), so the examples use `dotnet test --project`. The integration suite uses temporary directories and local fixtures; it does not require real package managers on the machine.

Command fixtures create `.cmd` launchers on Windows and executable shell launchers on Linux, and invoke PowerShell by its resolved absolute path. Package tests replace the inherited `PATH` with the fixture directory, including controlled `apt-get`, `dpkg-query` and `sudo` commands for Linux. WinGet process tests run on Windows because Unix exit codes cannot represent its HRESULT values; its result-contract tests run on both platforms.

If PowerShell is missing, an [official .NET tool installation](https://learn.microsoft.com/powershell/scripting/install/alternate-install-methods#install-as-a-net-global-tool) can stay inside the checkout. In a Linux shell:

```bash
dotnet tool install PowerShell --tool-path ./artifacts/tools
export PATH="$PWD/artifacts/tools:$PATH"
```

When sharing one checkout between Windows and WSL, keep Linux build outputs separate:

```bash
dotnet build Station.slnx -c Release --artifacts-path "$PWD/artifacts/linux-check"
dotnet test --root-directory artifacts/linux-check/bin/Station.UnitTests/release \
  --test-modules Station.UnitTests.dll
dotnet test --root-directory artifacts/linux-check/bin/Station.IntegrationTests/release \
  --test-modules Station.IntegrationTests.dll
```

`--test-modules` takes a pattern relative to `--root-directory`. The Windows and Ubuntu CI jobs run the normal project-based commands and publish distinct test artifacts.

For a CLI smoke test:

```powershell
dotnet src/Station/bin/Debug/net10.0/Station.dll --help
dotnet src/Station/bin/Debug/net10.0/Station.dll explain --config examples/workstation
```

## Publishing

To produce a self-contained Windows executable:

```powershell
dotnet publish src/Station/Station.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:UseAppHost=true
```

The output is `Station.exe` under `src/Station/bin/Release/net10.0/win-x64/publish/`.

## Documentation site

The site uses VitePress. Install Node.js and npm, then from the repository root:

```powershell
npm ci
npm run docs:dev
```

For a production build and local preview:

```powershell
npm run docs:build
npm run docs:preview
```

English pages live in `docs/guide/`; matching Russian pages live in `docs/ru/guide/`. Navigation and local search are configured in `docs/.vitepress/config.mts`. Keep both languages in sync when changing behavior descriptions.
