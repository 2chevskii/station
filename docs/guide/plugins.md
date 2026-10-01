# Plugins

Plugins distribute roles and .NET modules. Declare requirements in `station.yaml`, then run `station restore --config <directory>` to resolve them explicitly. `list`, `explain`, `validate`, `check` and `apply` restore declared plugins when needed.

## Requirements

Station supports five providers:

| Provider | Short form | Object form |
| --- | --- | --- |
| Local directory or archive under `<root>/plugins/` | `my-plugin` or `my-plugin.zip` | `{ provider: local, name: my-plugin }` |
| Git repository | `git://github.com/org/repo.git@v1.0.0` | `{ provider: git, url: 'https://example.com/repo.git', ref: main }` |
| Remote archive | `https://example.com/plugin.zip` | `{ provider: url, url: 'https://example.com/plugin.tar.gz' }` |
| NuGet package | `nuget://Company.Station.Plugin@1.2.3` | `{ provider: nuget, id: Company.Station.Plugin, version: 1.2.3 }` |
| npm package | `npm://@scope/station-plugin@1.2.3` | `{ provider: npm, id: '@scope/station-plugin', version: 1.2.3 }` |

Use YAML list entries for these forms:

```yaml
plugins:
  - my-plugin
  - provider: git
    url: https://github.com/example/station-plugin.git
    ref: v1.0.0
  - provider: nuget
    id: Company.Station.Plugin
    version: 1.2.3
    feed: https://api.nuget.org/v3/index.json
  - provider: npm
    id: "@scope/station-plugin"
    version: 1.2.3
    registry: https://registry.npmjs.org/
```

`feed` and `registry` are optional provider-specific fields. For a remote source without a recognizable archive or Git URL, use an explicit `provider` object. A local directory is used in place; archives and remote sources are cached.

## Manifest and exports

Every plugin root contains exactly one `manifest.yaml` or `manifest.yml`. The manifest determines the plugin identity; the requirement string does not.

```yaml
name: example.tools
version: 1.0.0
roles:
  - workstation
modules:
  - assemblies/Example.Plugin.dll:Example.Plugin.CleanupModule
  - assemblies/Example.Plugin.dll:Example.Plugin.SetupModule
```

An exported role lives at `roles/workstation/role.yaml` or `role.js`. Each module entry names an assembly path relative to the plugin root and an optional fully qualified public `ConfigModule` type. Explicit type names allow multiple modules in one assembly. A legacy short entry such as `cleanup` expects `modules/cleanup/cleanup.dll` with exactly one public module type. The module's short name comes from its `[Module]` attribute. Only manifest-listed roles and modules are exported.

The example exports `example.tools.workstation` as a role and modules under `example.tools.*`. Use full names when a short name is ambiguous. Role and module names are resolved independently.

## Cache location

The default cache root is `<config-root>/.station/plugin_cache`. Cached plugin contents are stored beneath `<manifest.name>/<manifest.version>/`. Override the cache root with these sources, from lowest to highest priority:

1. `vars.plugin_cache_dir` in configuration.
2. `CONFIG_TOOL_VAR_PLUGIN_CACHE_DIR` in the environment.
3. `--set plugin_cache_dir=<path>`.
4. `--plugin-cache-dir <path>`.

Relative override paths resolve from the configuration root. Local plugin directories remain at their source path.
