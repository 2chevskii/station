# Station

Station is a local .NET 10 tool for configuring a workstation from one desired-state project directory. It loads `station.yaml`, runs root tasks and selected roles, and supports built-in modules, local .NET modules, and plugins.

## Documentation

The bilingual VitePress documentation starts here:

- [English](docs/index.md) · [Getting started](docs/guide/getting-started.md) · [CLI](docs/guide/cli.md) · [Configuration](docs/guide/configuration.md)
- [Русский](docs/ru/index.md) · [Быстрый старт](docs/ru/guide/getting-started.md) · [Командная строка](docs/ru/guide/cli.md) · [Конфигурация](docs/ru/guide/configuration.md)

The [workstation example](examples/workstation) shows roles, variable files, templates, and files.

## Build and verify

```powershell
dotnet build Station.slnx -v minimal
dotnet test --project tests/Station.UnitTests/Station.UnitTests.csproj -v minimal
dotnet test --project tests/Station.IntegrationTests/Station.IntegrationTests.csproj -v minimal
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll --help
```

## Documentation site

With Node.js and npm installed:

```powershell
npm ci
npm run docs:dev
```

Use `npm run docs:build` to check both language versions and create the static site. The output is `docs/.vitepress/dist/`.
