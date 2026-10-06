# Configuration root

Station reads one directory as the configuration root. The required `station.yaml` is a YAML representation of its in-memory configuration model.

```text
workstation/
├── station.yaml
├── vars/
│   ├── 00-common.yaml
│   └── 10-machine.js
├── files/
├── templates/
├── roles/
│   └── git/
│       ├── defaults.yaml
│       ├── role.yaml
│       ├── files/
│       └── templates/
├── modules/
│   └── MyModule/
│       └── MyModule.dll
└── plugins/
```

`station.yaml` is required at the root. Its `roles` list must contain at least one role name, and each selected role must have a definition. Other root fields are optional.

## `station.yaml`

```yaml
roles:
  - git
  - team.workstation

plugins:
  - provider: local
    name: team-plugin

vars:
  user_name: Example User
  git:
    default_branch: main

tasks:
  - name: Install Git
    winget:
      id: Git.Git
      state: present
```

| Field | Type | Description |
| --- | --- | --- |
| `roles` | Nonempty list of strings | Roles to run, in order. Names can be fully qualified or unambiguous short names. |
| `plugins` | List of strings or objects | Plugin source requirements; see [Plugins](./plugins.md). |
| `vars` | Mapping | Root variable overrides. |
| `tasks` | List of task objects | Tasks executed before all selected roles. |

The `roles/`, `modules/` and `plugins/` directories make local components available; listing a role under `roles` in `station.yaml` selects it for execution. A role or module's fully qualified name includes its provider: local components use `station.local`, built-in modules use `station.builtin`, and plugins use their manifest `name`. Short names work when unique within their own role or module namespace.

## Artifacts

`files/` holds input files for `copy`. `templates/` holds Scriban templates for `template`. Roles may also contain their own `files/` and `templates/`; a role artifact takes precedence over a root artifact with the same logical name. Templates are addressed without the `.scriban` or `.sbn` suffix.

For example, `template: git/config` may resolve `templates/git/config.scriban`. See [Variables and templates](./variables.md) for rendering.

## File formats

The root manifest is `station.yaml`. Variable files in `vars/` may be `.yaml`, `.js` or `.lua`. A role uses one of `role.yaml`, `role.js` or `role.lua`, and optionally one of `defaults.yaml`, `defaults.js` or `defaults.lua`. Station rejects multiple formats for the same role artifact, and root variable files with the same base name across formats are ambiguous. See [Roles and tasks](./roles-and-tasks.md), [JavaScript roles](./javascript-roles.md) and [Lua roles](./lua-roles.md).
