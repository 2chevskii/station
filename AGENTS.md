# AGENTS

This repository contains `Station`, a local desired-state workstation configuration tool implemented
in .NET.

## Current architecture

- `Station.Abstractions`
  Shared contracts and path conversion helpers. Module projects are allowed to depend on this
  project.
- `Station.Core`
  Orchestration plus internal `Configuration`, `Execution`, and plugin manifest validation.
  Group related records, contracts, and behavior by domain responsibility. Keep discovery,
  loading, roles, modules, planning, execution, and restoration in focused folders.
  Do not introduce generic `Models/`, `Services/`, or `Interfaces/` folders.
- `Station.Plugins.Abstractions`
  Plugin requirement and manifest document contracts shared by Core and plugin restoration.
- `Station.Plugins`
  Plugin source restoration, caching, and provider implementations. This project must not depend on
  Core.
- `Station.Utilities`
  Shared process execution used by Core and Plugins.
- `Station.Configuration.Abstractions`
  Shared configuration document contracts, artifact records, and value conversion used by format
  loaders and Core.
- `Station.Configuration.Yaml`
  YAML document loading and Scriban template rendering.
- `Station.Configuration.JavaScript`
  JavaScript document loading and runtime.
- `Station.Modules.*`
  Concrete builtin modules. These projects must depend on `Station.Abstractions` only, not on
  `Station.Core`.
- `Station`
  Thin command-line frontend.

## Important project rules

- Do not reintroduce `profile`-based config loading.
- Do not reintroduce task-level `target` into core execution.
- Do not move builtin modules back into `Core`.
- Do not run modifying git operations in parallel.
  This includes commands such as `git add`, `git commit`, `git mv`, `git rm`, `git checkout`,
  `git switch`, and similar index- or ref-mutating operations.
  Run those sequentially to avoid `index.lock` races.
- Do not run `dotnet build`, `dotnet test`, or `dotnet restore` in parallel on overlapping or
  transitively related projects.
  Build/test/restore commands may race on shared `obj/` and `bin/` outputs or on restore state.
  Run them sequentially whenever the target projects share dependencies, outputs, or a common
  solution graph.
- Keep the task model Ansible-like:
    - optional `name`
    - exactly one module key at task root
    - module parameters nested under that key
- Keep the in-memory .NET object model as the source of truth. YAML is only one serialization
  format.
- Plugin identity comes from `manifest.yaml|yml`, not from the requirement string.
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
- `role.yaml` as a task array or an object with required `tasks` and optional `description` and
  `defaults`
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
manifest.yaml|yml
roles/<RoleName>/role.yaml
modules/<ModuleName>/<ModuleName>.dll
```

Plugin manifests may also export modules as `<relative assembly path>:<fully qualified type name>`,
including multiple types from one DLL. The module name comes from `[Module]`.

## Plugin restore

Supported provider names:

- `local`
- `git`
- `url`
- `nuget`
- `npm`

Remote and local archive plugins are cached under:

```text
<root>/.station/plugin_cache/<manifest.name>/<manifest.version>/<plugin contents>
```

The cache root can be overridden by `vars.plugin_cache_dir`, `CONFIG_TOOL_VAR_PLUGIN_CACHE_DIR`,
`--set plugin_cache_dir=<path>`, or `--plugin-cache-dir <path>` in that precedence order from lowest
to highest. Relative paths resolve from the config root.

`restore` is explicit in CLI, and `validate/check/apply` perform implicit restore.

## Module architecture

- Modules are declared with `[Module]` attributes, not descriptor properties.
- Module parameters are declared with `[Parameter]` attributes and bound by the engine.
- Modules receive constructor-injected `ModuleContext`.
- Builtin file-oriented modules are `copy` and `template`; do not reintroduce the old `dotfile`
  module.

## Verification expectations

Before closing substantive changes, prefer to run:

```powershell
dotnet build Station.slnx -v minimal
dotnet test --project tests/Station.UnitTests/Station.UnitTests.csproj -v minimal
dotnet test --project tests/Station.IntegrationTests/Station.IntegrationTests.csproj -v minimal
```

If CLI behavior changed, also smoke-test:

```powershell
dotnet src/Station/bin/Debug/net10.0/Station.dll --help
```

If `explain` behavior changed, also smoke-test:

```powershell
dotnet src/Station/bin/Debug/net10.0/Station.dll explain --config examples/workstation
```

## Example root

Use [examples/workstation](examples/workstation)
as the canonical example of the current target config structure.
