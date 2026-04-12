# AGENTS

This repository contains `Station`, a local desired-state workstation configuration tool implemented in .NET.

## Current architecture

- `Station.Abstractions`
  Shared contracts only. Module projects are allowed to depend on this project.
- `Station.Core`
  Orchestration plus internal `Configuration`, `Execution`, and `Plugins` subdomains.
  Keep domain records in `Models/`, orchestration/loaders in `Services/`, and plugin restore strategies in `Plugins/Providers/`.
- `Station.Modules.*`
  Concrete builtin modules. These projects must depend on `Station.Abstractions` only, not on `Station.Core`.
- `Station.Cli`
  Thin command-line frontend.

## Important project rules

- Do not reintroduce `profile`-based config loading.
- Do not reintroduce task-level `target` into core execution.
- Do not move builtin modules back into `Core`.
- Do not run modifying git operations in parallel.
  This includes commands such as `git add`, `git commit`, `git mv`, `git rm`, `git checkout`, `git switch`, and similar index- or ref-mutating operations.
  Run those sequentially to avoid `index.lock` races.
- Do not run `dotnet build`, `dotnet test`, or `dotnet restore` in parallel on overlapping or transitively related projects.
  Build/test/restore commands may race on shared `obj/` and `bin/` outputs or on restore state.
  Run them sequentially whenever the target projects share dependencies, outputs, or a common solution graph.
- Keep the task model Ansible-like:
  - optional `name`
  - exactly one module key at task root
  - module parameters nested under that key
- Keep the in-memory .NET object model as the source of truth. YAML is only one serialization format.
- Plugin identity comes from `plugin-manifest.yaml|yml`, not from the declaration string.
- Keep plugin restore provider-specific behavior behind separate provider implementations.
- Keep large loaders split by responsibility. Do not collapse `ConfigLoader` back into a monolith.
- Role/module resolution rules:
  - every artifact has an FQN
  - short names are convenience only when unambiguous
  - role and module namespaces are separate

## Config model

Root config currently supports:

- `roles`
- `plugins`
- `vars`
- `tasks`

Role layout currently supports:

- `defaults.yaml`
- `role.yaml`
- `files/`
- `templates/`

Root layout also supports:

- `files/`
- `templates/`

Local module layout:

```text
modules/<ModuleName>/<ModuleName>.dll
```

Plugin manifest layout:

```text
plugin-manifest.yaml|yml
roles/<RoleName>/role.yaml
modules/<ModuleName>/<ModuleName>.dll
```

## Plugin restore

Supported provider names:

- `local`
- `git`
- `url`
- `nuget`
- `npm`

Non-local plugins are cached under:

```text
<root>/.station/plugin_cache
```

`restore` is explicit in CLI, and `validate/check/apply` perform implicit restore.

## Module architecture

- Modules are declared with `[Module]` attributes, not descriptor properties.
- Module parameters are declared with `[Parameter]` attributes and bound by the engine.
- Modules receive constructor-injected `ModuleContext`.
- Builtin file-oriented modules are `copy` and `template`; do not reintroduce the old `dotfile` module.

## Verification expectations

Before closing substantive changes, prefer to run:

```powershell
dotnet build Station.slnx -v minimal
dotnet test tests/Station.Tests/Station.Tests.csproj -v minimal
```

If CLI behavior changed, also smoke-test:

```powershell
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll --help
```

If `explain` behavior changed, also smoke-test:

```powershell
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll explain --config examples/workstation
```

## Example root

Use [examples/workstation](C:\Users\dvchevskii\source\experiments\config-tool\examples\workstation) as the canonical example of the current target config structure.
