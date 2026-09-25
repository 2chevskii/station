# Station

`Station` is a local desired-state tool for configuring a workstation from one project directory.

The current codebase already implements the new architecture we planned:

- root config with `roles`, `plugins`, `vars`, and root-level `tasks`
- roles as declarative task lists in `role.yaml`
- modules as atomic execution units
- modules discovered from `[Module]` metadata and instantiated lazily per task
- declarative module parameters via `[Parameter]` with engine-level binding/validation
- local modules from `modules/<Name>/<Name>.dll`
- plugin restore/cache with provider strategies
- builtin modules extracted into separate projects and loaded by core by default

## Repository structure

```text
src/
  Station.Abstractions/
    Artifacts/
    Configuration/
    Execution/
    FileSystem/
    Modules/
  Station.Configuration.Abstractions/
    Documents/
    Values/
  Station.Configuration.JavaScript/
  Station.Configuration.Yaml/
  Station.Core/
    Configuration/
      Artifacts/
      Discovery/
      Loading/
      Plugins/
      Roles/
      Values/
    Execution/
      Modules/
      Planning/
      Privileges/
      Roles/
      Running/
    Plugins/
      Restoration/
  Station.Plugins.Abstractions/
    Manifests/
    Sources/
  Station.Plugins/
    Caching/
    Manifests/
    Providers/
    Restoration/
  Station.Utilities/
    Processes/
  Station.Cli/
  Modules/
    Station.Modules.Builtin.Apt/
    Station.Modules.Builtin.Dotfile/
    Station.Modules.Builtin.Script/
    Station.Modules.Builtin.Scoop/
    Station.Modules.Builtin.Winget/
tests/
  Station.UnitTests/
  Station.IntegrationTests/
  Station.Testing/
examples/
  workstation/
```

## Build and test

```powershell
dotnet build Station.slnx -v minimal
dotnet test tests/Station.UnitTests/Station.UnitTests.csproj -v minimal
dotnet test tests/Station.IntegrationTests/Station.IntegrationTests.csproj -v minimal
```

Generate MTP v2 coverage and TRX artifacts locally:

```powershell
$root = (Get-Location).Path
dotnet test tests/Station.UnitTests/Station.UnitTests.csproj -v minimal -- `
  --coverage `
  --coverage-output-format cobertura `
  --coverage-output (Join-Path $root "artifacts/test-results/unit/coverage.xml") `
  --report-trx `
  --report-trx-filename results.trx

dotnet test tests/Station.IntegrationTests/Station.IntegrationTests.csproj -v minimal -- `
  --coverage `
  --coverage-output-format cobertura `
  --coverage-output (Join-Path $root "artifacts/test-results/integration/coverage.xml") `
  --report-trx `
  --report-trx-filename results.trx
```

To publish a self-contained executable:

```powershell
dotnet publish src/Station.Cli/Station.Cli.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

## CLI

```text
station restore      --config <dir> [--set key=value ...] [--plugin-cache-dir <path>]
station explain      --config <dir> [--set key=value ...] [--plugin-cache-dir <path>]
station list roles   --config <dir> [--set key=value ...] [--plugin-cache-dir <path>]
station list modules --config <dir> [--set key=value ...] [--plugin-cache-dir <path>]
station validate     --config <dir> [--set key=value ...] [--plugin-cache-dir <path>]
station check        --config <dir> [--set key=value ...] [--plugin-cache-dir <path>]
station apply        --config <dir> [--set key=value ...] [--plugin-cache-dir <path>]
```

- `restore` resolves plugin requirements and caches remote and local archive plugins under `<root>/.station/plugin_cache/<manifest.name>/<manifest.version>/`
- `explain` prints an ASCII plan of tasks, bound parameters, and currently visible variables
- `list roles` and `list modules` restore declared plugins and show all available names, descriptions, and sources without running tasks
- `validate` restores plugins if needed, resolves roles/modules, and runs inspection
- `check` performs the same validation and runs dry-run execution
- `apply` mutates the machine in declared order

`Station.Core` groups configuration, execution, and plugin restoration by responsibility. Plugin source strategies live under `Station.Plugins/Providers`.

## Config root

Current target layout:

```text
station.yaml
files/
templates/
vars/
  00-common.yaml
  10-workstation.yaml
roles/
  git/
    metadata.yaml
    defaults.yaml
    role.yaml
    files/
    templates/
modules/
  MyModule/
    MyModule.dll
plugins/
  my_plugin/
  my_plugin.zip
```

`station.yaml` is currently the YAML serialization of the in-memory root config object. The internal model is format-agnostic, so YAML is only the first supported authoring format.

Top-level fields:

- `roles`: ordered list of role names or role FQNs
- `plugins`: plugin requirements
- `vars`: mapping of root variable overrides
- `tasks`: ordered root-level tasks that run before roles

A role may contain `metadata.yaml` or `metadata.yml` with `description: ...`. Listing shows `—` when a role has no description. Module descriptions come from `[Module]` attributes. Sources identify the provider and the role directory or module assembly.

Example:

```yaml
roles:
  - git
  - terminal

plugins:
  - my_plugin
  - provider: git
    url: https://git.example.com/team/plugin.git
    ref: v1.0.0
  - nuget://Company.Station.Plugin.Dev@1.2.3

vars:
  user_name: Demo Operator
  user_email: demo@example.com

tasks:
  - winget:
      id: Git.Git
      state: present
```

Variable precedence:

1. `roles/<role>/defaults.yaml`
2. `vars/*.yaml` in alphabetical order
3. root `vars`
4. environment variables with the `CONFIG_TOOL_VAR_` prefix
5. CLI `--set key=value`

## Roles and tasks

Each role lives under `roles/<RoleName>/`.

Supported files/directories today:

- `defaults.yaml`
- `role.yaml`
- `files/`
- `templates/`
- root-level `files/`
- root-level `templates/`

`role.yaml` is currently a root-level list of tasks:

```yaml
- name: Install Git
  winget:
    id: Git.Git
    state: present

- template:
    template: git/.gitconfig
    destination: C:\Users\demo\.gitconfig

- copy:
    file: git/.gitignore_global
    destination: C:\Users\demo\.gitignore_global
```

Rules:

- `name` is optional
- role task default name: `<role_name>.tasks.<one-based index>`
- root task default name: `station.tasks.<one-based index>`
- each task must declare exactly one module key
- module parameters are nested under that module key
- short-form task arguments are intentionally deferred; canonical form is object-based
- modules are discovered from `[Module]` attributes and instantiated lazily per task execution
- module parameters are declared with `[Parameter]` attributes and validated before execution
- variable scopes remain typed in memory; environment and CLI overrides are added as string overlays
- string values in config and templates use Scriban syntax such as `{{ user_name }}`
- templates live under `templates/` and are addressed by logical name; `template: git/.gitconfig` resolves `templates/git/.gitconfig.scriban|.sbn`
- files and templates in a role override artifacts with the same logical name from the config root

## Modules

Modules are atomic execution capabilities.

Resolution rules:

- every module has an FQN, for example `station.builtin.template`
- short names like `template` are allowed only when unambiguous

Sources of modules:

- builtin modules under the synthetic provider `station.builtin`
- local modules under the synthetic provider `station.local`
- plugin-provided modules under the namespace named in `manifest.yaml` or `manifest.yml`
- builtin file-oriented modules currently follow the Ansible-like split into `copy` and `template`

Local modules use:

```text
modules/<ModuleName>/<ModuleName>.dll
```

## Plugins

Plugins can export any number of roles and modules.

Each plugin root must contain exactly one manifest:

- `manifest.yaml`
- or `manifest.yml`

Initial manifest shape:

```yaml
name: example.plugin
version: 1.0.0
roles:
  - workstation
modules:
  - assemblies/Example.Plugin.dll:Example.Plugin.ChocolateyModule
  - assemblies/Example.Plugin.dll:Example.Plugin.ScoopModule
```

Rules:

- `name` is the provider namespace
- `roles` contains short names
- `modules` selects public `ConfigModule` types with `<assembly path relative to plugin root>:<fully qualified type name>`; several entries may select types from one DLL
- the module's short name comes from its `[Module]` attribute
- legacy short module entries still resolve `modules/<ModuleName>/<ModuleName>.dll` when that DLL contains one module type
- exported role path: `roles/<RoleName>/role.yaml`
- exported module assembly must exist inside the plugin root
- if both manifest extensions exist, loading fails
- non-exported artifacts under `roles/` and `modules/` are treated as internal

### Plugin providers

Supported requirement styles.
Internally, plugin restore is implemented through provider strategies rather than a central provider enum switch:

Local:

```yaml
plugins:
  - my_plugin
  - provider: local
    name: my_plugin.zip
```

Git:

```yaml
plugins:
  - git://github.com/org/repo.git@v1.0.0
  - provider: git
    url: https://git.example.com/plugin.git
    ref: main
```

Remote archive:

```yaml
plugins:
  - https://example.com/my_plugin.zip
  - provider: url
    url: https://example.com/releases/my_plugin.tar.gz
```

NuGet:

```yaml
plugins:
  - nuget://Company.Station.Plugin.Dev@1.2.3
  - provider: nuget
    id: Company.Station.Plugin.Dev
    version: 1.2.3
    feed: https://api.nuget.org/v3/index.json
```

npm:

```yaml
plugins:
  - npm://@scope/station-plugin@1.2.3
  - provider: npm
    id: "@scope/station-plugin"
    version: 1.2.3
    registry: https://registry.npmjs.org/
```

Remote and local archive plugins are restored into:

```text
<root>/.station/plugin_cache/<manifest.name>/<manifest.version>/<plugin contents>
```

Set `vars.plugin_cache_dir` in `station.yaml`, `CONFIG_TOOL_VAR_PLUGIN_CACHE_DIR` in the environment, or `--plugin-cache-dir <path>` to change the cache root. `--set plugin_cache_dir=<path>` also works. A relative path is resolved from the config root. The priority is command-line flag, `--set`, environment variable, then configuration variables. Local plugin directories are used directly and are unaffected.

## Test architecture

- `tests/Station.UnitTests` contains fast public-surface unit tests on xUnit v3 + MTP v2
- `tests/Station.IntegrationTests` exercises the CLI, engine, plugin restore, modules, and process integration
- `tests/Station.Testing` contains shared helpers and isolation fixtures
- assertions use FluentAssertions
- integration tests use temporary directories, local HTTP fixtures, and explicit command shims so they do not depend on the real workstation filesystem or real `winget`/`scoop`/`wsl` executables
- GitHub Actions publishes Cobertura coverage, HTML coverage output, TRX, and CTRF JSON artifacts

## Example root

See [examples/workstation](C:\Users\dvchevskii\source\experiments\config-tool\examples\workstation) for a sample config root that matches the current target structure.
