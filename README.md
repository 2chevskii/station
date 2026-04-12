# Station

`Station` is a local desired-state tool for configuring a workstation from one project directory.

The current codebase already implements the new architecture we planned:

- root config with `roles`, `plugins`, `vars`, and root-level `tasks`
- roles as declarative task lists in `role.yaml`
- modules as atomic execution units
- local modules from `modules/<Name>/<Name>.dll`
- plugin restore/cache with multiple providers
- builtin modules extracted into separate projects and loaded by core by default

## Repository structure

```text
src/
  Station.Abstractions/
  Station.Core/
  Station.Cli/
  Station.Modules.Apt/
  Station.Modules.Dotfile/
  Station.Modules.Script/
  Station.Modules.Scoop/
  Station.Modules.Winget/
tests/
examples/
  workstation/
```

## Build and test

```powershell
dotnet build Station.slnx -v minimal
dotnet test tests/Station.Tests/Station.Tests.csproj -v minimal
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
station restore  --config <dir> [--set key=value ...]
station explain  --config <dir> [--set key=value ...]
station validate --config <dir> [--set key=value ...]
station check    --config <dir> [--set key=value ...]
station apply    --config <dir> [--set key=value ...]
```

- `restore` resolves declared plugins and stores non-local ones under `<root>/.station/plugin_cache`
- `explain` prints an ASCII plan of tasks, bound parameters, and currently visible variables
- `validate` restores plugins if needed, resolves roles/modules, and runs inspection
- `check` performs the same validation and runs dry-run execution
- `apply` mutates the machine in declared order

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
- `plugins`: plugin declarations
- `vars`: mapping of root variable overrides
- `tasks`: ordered root-level tasks that run before roles

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
- plugin-provided modules under the provider declared by `plugin-manifest`

Local modules use:

```text
modules/<ModuleName>/<ModuleName>.dll
```

## Plugins

Plugins can export any number of roles and modules.

Each plugin root must contain exactly one manifest:

- `plugin-manifest.yaml`
- or `plugin-manifest.yml`

Initial manifest shape:

```yaml
name: example.plugin
version: 1.0.0
roles:
  - workstation
modules:
  - chocolatey
```

Rules:

- `name` is the provider namespace
- `roles` and `modules` contain short names
- exported role path: `roles/<RoleName>/role.yaml`
- exported module path: `modules/<ModuleName>/<ModuleName>.dll`
- if both manifest extensions exist, loading fails
- non-exported artifacts under `roles/` and `modules/` are treated as internal

### Plugin providers

Supported declaration styles:

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

Non-local plugins are restored into:

```text
<root>/.station/plugin_cache
```

## Example root

See [examples/workstation](C:\Users\dvchevskii\source\experiments\config-tool\examples\workstation) for a sample config root that matches the current target structure.
