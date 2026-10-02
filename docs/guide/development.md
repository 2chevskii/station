# Development

## Repository layout

Station targets .NET 10. Its projects separate contracts, configuration loading, execution, plugin restoration and built-in modules:

| Project | Responsibility |
| --- | --- |
| `Station.Abstractions` | Module, variable and artifact contracts. |
| `Station.Configuration.Abstractions` | Configuration document contracts and values. |
| `Station.Configuration.Yaml` | YAML loading and Scriban rendering. |
| `Station.Configuration.JavaScript` | Jint-based variable and role loading. |
| `Station.Core` | Discovery, binding, planning and task execution. |
| `Station.Plugins.Abstractions` | Plugin manifest and requirement contracts. |
| `Station.Plugins` | Provider-specific restore and caching. |
| `Station.Utilities` | Shared process and privilege utilities. |
| `Station.Modules.*` | Built-in modules. |
| `Station.Cli` | Command-line entry point and output. |

The core task model is Ansible-like: optional name, one module key and a nested parameter object. YAML is a serialization format; the in-memory .NET model is the source of truth.

## Dependency injection

`Station.Cli` composes services through `StationServices.AddStation` and creates and disposes the container in `Program.cs`. Core and plugin services receive their dependencies through constructors. `AddStationCore` and `AddStationPlugins` register their respective internal services without creating a container.

Configuration loaders, plugin providers, the template engine, process runner, privilege detector, HTTP client and time provider are shared services. Modules are created for each task through `ActivatorUtilities`, with a fresh `ModuleContext` and any additional constructor dependencies resolved from DI. Integration tests use the CLI registrations and can replace individual dependencies before building the container.

## Build and test

From the repository root:

```powershell
dotnet build Station.slnx -v minimal
dotnet test --project tests/Station.UnitTests/Station.UnitTests.csproj -v minimal
dotnet test --project tests/Station.IntegrationTests/Station.IntegrationTests.csproj -v minimal
```

Run build and test commands sequentially because the projects share output and restore state. The repository selects [Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/tools/dotnet-test-mtp), so the examples use `dotnet test --project`. The integration suite uses temporary directories and local fixtures; it does not require real package managers on the machine.

For a CLI smoke test:

```powershell
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll --help
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll explain --config examples/workstation
```

## Publishing

To produce a self-contained Windows executable:

```powershell
dotnet publish src/Station.Cli/Station.Cli.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:UseAppHost=true
```

The output is `Station.Cli.exe` under `src/Station.Cli/bin/Release/net10.0/win-x64/publish/`.

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
