# Variables and templates

Variables are typed objects in Station's configuration model. YAML mappings, lists, numbers and booleans retain their types until a module parameter is bound. Environment variables and CLI overrides enter as strings.

## Merge order

For a task inside a role, later sources override earlier ones:

1. The role's `defaults.yaml` or `defaults.js`.
2. Inline `defaults` in an object-form `role.yaml`.
3. Root `vars/*.yaml` and `vars/*.js`, in case-insensitive filename order.
4. `vars` in `station.yaml`.
5. Environment variables beginning with `CONFIG_TOOL_VAR_`.
6. Repeated CLI `--set key=value` values.

Station also supplies `user_home` from the current user's profile directory. It is merged after the sources above, so treat it as a reserved variable.

`vars/10-machine.yaml`:

```yaml
git:
  default_branch: main
terminal_theme: Campbell
```

`roles/git/defaults.yaml`:

```yaml
git_config_path: "{{ user_home }}/.gitconfig"
```

Nested environment and CLI keys use dots:

```powershell
station check --config ./workstation --set git.default_branch=develop
```

The CLI value overrides `git.default_branch` from the file. For a plain environment name, use `CONFIG_TOOL_VAR_TERMINAL_THEME`. An environment key such as `CONFIG_TOOL_VAR_GIT.DEFAULT_BRANCH` sets a nested value; how to set a name containing a dot depends on the shell.

## Scriban rendering

String values can contain Scriban expressions:

```yaml
tasks:
  - template:
      template: git/config
      destination: "{{ user_home }}/.gitconfig"
```

Template files also use Scriban. For example, `templates/git/config.scriban`:

```text
[init]
    defaultBranch = {{ git.default_branch }}
```

Module parameters are resolved against the task's variable scope before binding. A role task sees merged role defaults and root variables; a root task sees root variables. For a preview of visible values, use `station explain --config <directory>`.
