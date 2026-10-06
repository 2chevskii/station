# Troubleshooting

Start with `station explain --config <directory>` to inspect names, parameters and visible variables. Use `station list roles` or `station list modules` to see discovered components. `station validate` reports parameter, artifact and privilege diagnostics without executing tasks.

## Configuration cannot be loaded

Check that `--config` points to an existing directory containing `station.yaml`, and that its `roles` field is a nonempty list. Every selected role needs a `role.yaml`, `role.js` or `role.lua` in its role directory. A task needs exactly one module key whose value is a parameter object.

If Station reports ambiguous variable files or role files, remove the duplicate format: keep one `role.*` and one `defaults.*` file per role, and one file per basename in `vars/`, across YAML, JavaScript and Lua.

## A role or module name is ambiguous

List available names and use the fully qualified name reported there. Local roles and modules start with `station.local`; built-in modules start with `station.builtin`; plugin exports start with the manifest's `name`.

```powershell
station list roles --config ./workstation
station list modules --config ./workstation
```

Role and module namespaces are independent. A short name must be unique within its respective namespace.

## A file, template or script is missing

For `copy`, check `files/<logical-name>` in the role or configuration root. For `template`, check `templates/<logical-name>.scriban` or `.sbn`. For `script`, `path` must stay inside the task root: the configuration root for root tasks or the role root for role tasks. `script` also requires its `runner` on `PATH`.

## A plugin cannot be restored

Run `station restore --config <directory>` to isolate restoration errors. Local plugin requirements resolve under `<config-root>/plugins/`. Each plugin needs exactly one `manifest.yaml` or `manifest.yml` with a nonempty `name` and `version`. Confirm every exported role and module exists at its declared path. Check network access and source coordinates for Git, URL, NuGet and npm providers.

To relocate downloaded content, set `--plugin-cache-dir <path>`; see [Plugins](./plugins.md#cache-location).

## A task requires elevation or changes unexpectedly

Run `validate` from an elevated terminal when inspection reports an administrator requirement. `winget` with `scope: machine`, Scoop with `global: true`, and protected file destinations may require it. Use `check` to review expected changes before `apply`. Custom `script` and .NET modules must implement check mode without making changes; inspect their implementation if a dry run mutates the machine.
