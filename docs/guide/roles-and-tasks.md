# Roles and tasks

A role groups ordered tasks with optional defaults and file artifacts. Select roles in the root `roles` list. A local role lives at `roles/<name>/`; a plugin can export roles under its own namespace.

## YAML role forms

`role.yaml` can be a task list:

```yaml
- name: Install Git
  winget:
    id: Git.Git
    state: present
```

It can also be an object with required `tasks`, optional `description` and optional `defaults`:

```yaml
description: Configure Git preferences.
defaults:
  git_config_path: "{{ user_home }}/.gitconfig"
tasks:
  - name: Render Git config
    template:
      template: git/config
      destination: "{{ git_config_path }}"
```

The separate `defaults.yaml`, `defaults.js` or `defaults.lua` is optional. Inline YAML defaults override matching values from that file; root variables override both. The scripting formats are described in [JavaScript roles](./javascript-roles.md) and [Lua roles](./lua-roles.md).

## Task contract

Each task is an object with an optional `name` and **exactly one module key**. Parameters belong inside that key and must be an object. Do not put a `target` key on a task.

```yaml
- name: Install ripgrep
  scoop:
    name: ripgrep
    state: present
```

Without `name`, root tasks are named `station.tasks.1`, `station.tasks.2`, and so on. Role tasks use `<role-name>.tasks.1`, `<role-name>.tasks.2`, etc. Root tasks run first, followed by each selected role's tasks in the order declared.

Module and role references accept fully qualified names, such as `station.builtin.copy` and `station.local.git`. A short reference, such as `copy` or `git`, resolves only if one component with that short name exists in its namespace. If multiple plugins export the same short name, use the fully qualified name.

## Inspection and execution

Station processes tasks in order. For `validate`, it binds and inspects each task without executing it. For `check` and `apply`, it binds and inspects a task, then executes that task before moving to the next. `check` uses `ExecutionMode.Check`; `apply` uses `ExecutionMode.Apply`. Modules that need elevated permissions report that during inspection. An apply failure stops later tasks.
