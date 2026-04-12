# AGENTS

This repository contains `config-tool`, a local desired-state workstation configuration tool implemented in .NET.

## Current architecture

- `ConfigTool.Abstractions`
  Shared contracts only. Module projects are allowed to depend on this project.
- `ConfigTool.Core`
  Orchestration, config loading, plugin restore/cache, role/module resolution, CLI execution flow.
- `ConfigTool.Modules.*`
  Concrete builtin modules. These projects must depend on `ConfigTool.Abstractions` only, not on `ConfigTool.Core`.
- `ConfigTool.Cli`
  Thin command-line frontend.

## Important project rules

- Do not reintroduce `profile`-based config loading.
- Do not reintroduce task-level `target` into core execution.
- Do not move builtin modules back into `Core`.
- Keep the task model Ansible-like:
  - optional `name`
  - exactly one module key at task root
  - module parameters nested under that key
- Keep the in-memory .NET object model as the source of truth. YAML is only one serialization format.
- Plugin identity comes from `plugin-manifest.yaml|yml`, not from the declaration string.
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

Supported provider kinds:

- `local`
- `git`
- `url`
- `nuget`
- `npm`

Non-local plugins are cached under:

```text
<root>/.configtool/plugin_cache
```

`restore` is explicit in CLI, and `validate/check/apply` perform implicit restore.

## Verification expectations

Before closing substantive changes, prefer to run:

```powershell
dotnet build ConfigTool.slnx -v minimal
dotnet test tests/ConfigTool.Tests/ConfigTool.Tests.csproj -v minimal
```

If CLI behavior changed, also smoke-test:

```powershell
dotnet src/ConfigTool.Cli/bin/Debug/net10.0/ConfigTool.Cli.dll --help
```

## Example root

Use [examples/workstation](C:\Users\dvchevskii\source\experiments\config-tool\examples\workstation) as the canonical example of the current target config structure.
