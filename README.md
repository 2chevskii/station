# config-tool

`config-tool` is a local desired-state tool for configuring a workstation from one project directory.

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
  ConfigTool.Abstractions/
  ConfigTool.Core/
  ConfigTool.Cli/
  ConfigTool.Modules.Apt/
  ConfigTool.Modules.Dotfile/
  ConfigTool.Modules.Script/
  ConfigTool.Modules.Scoop/
  ConfigTool.Modules.Winget/
tests/
examples/
  workstation/
```

## Build and test

```powershell
dotnet build ConfigTool.slnx -v minimal
dotnet test tests/ConfigTool.Tests/ConfigTool.Tests.csproj -v minimal
```

To publish a self-contained executable:

```powershell
dotnet publish src/ConfigTool.Cli/ConfigTool.Cli.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

## CLI

```text
config-tool restore  --config <dir> [--set key=value ...]
config-tool validate --config <dir> [--set key=value ...]
config-tool check    --config <dir> [--set key=value ...]
config-tool apply    --config <dir> [--set key=value ...]
```

- `restore` resolves declared plugins and stores non-local ones under `<root>/.configtool/plugin_cache`
- `validate` restores plugins if needed, resolves roles/modules, and runs inspection
- `check` performs the same validation and runs dry-run execution
- `apply` mutates the machine in declared order

## Config root

Current target layout:

```text
config-tool.yaml
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

`config-tool.yaml` is currently the YAML serialization of the in-memory root config object. The internal model is format-agnostic, so YAML is only the first supported authoring format.

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
  - nuget://Company.ConfigTool.Plugin.Dev@1.2.3

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

`role.yaml` is currently a root-level list of tasks:

```yaml
- name: Install Git
  winget:
    id: Git.Git
    state: present

- dotfile:
    source: templates/git/.gitconfig
    destination: C:\Users\demo\.gitconfig
```

Rules:

- `name` is optional
- role task default name: `<role_name>.tasks.<one-based index>`
- root task default name: `config-tool.tasks.<one-based index>`
- each task must declare exactly one module key
- module parameters are nested under that module key
- short-form task arguments are intentionally deferred; canonical form is object-based

## Modules

Modules are atomic execution capabilities.

Resolution rules:

- every module has an FQN, for example `configtool.builtin.dotfile`
- short names like `dotfile` are allowed only when unambiguous

Sources of modules:

- builtin modules under the synthetic provider `configtool.builtin`
- local modules under the synthetic provider `configtool.local`
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
  - nuget://Company.ConfigTool.Plugin.Dev@1.2.3
  - provider: nuget
    id: Company.ConfigTool.Plugin.Dev
    version: 1.2.3
    feed: https://api.nuget.org/v3/index.json
```

npm:

```yaml
plugins:
  - npm://@scope/configtool-plugin@1.2.3
  - provider: npm
    id: "@scope/configtool-plugin"
    version: 1.2.3
    registry: https://registry.npmjs.org/
```

Non-local plugins are restored into:

```text
<root>/.configtool/plugin_cache
```

## Example root

See [examples/workstation](C:\Users\dvchevskii\source\experiments\config-tool\examples\workstation) for a sample config root that matches the current target structure.
