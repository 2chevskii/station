# CLI reference

The commands below use `station` as shorthand for the CLI. The published Windows file is `Station.Cli.exe`; during development, use `dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll` from the repository root.

```text
station restore       --config <directory> [options]
station explain       --config <directory> [options]
station list roles    --config <directory> [options]
station list modules  --config <directory> [options]
station validate      --config <directory> [options]
station check         --config <directory> [options]
station apply         --config <directory> [options]
```

| Command | Behavior |
| --- | --- |
| `restore` | Resolves declared plugins and reports their source and location. |
| `explain` | Displays the static task tree, bound parameters and visible variables. |
| `list roles` | Lists available role names, descriptions and sources. |
| `list modules` | Lists available module names, descriptions and sources. |
| `validate` | Restores plugins, resolves tasks, binds parameters and runs module inspection. |
| `check` | Inspects and runs each task in check mode to report expected changes. |
| `apply` | Inspects and executes each task in order. |

All commands accept these options:

| Option | Meaning |
| --- | --- |
| `--config <directory>` | Configuration root; defaults to the current working directory. |
| `--set key=value` | Override a variable. Repeat the option for multiple values. Nested keys use dots, for example `--set git.user=Alex`. Values enter as strings. |
| `--plugin-cache-dir <path>` | Override the plugin cache root. Relative paths resolve from the configuration root. |

Use `station <command> --help` for command-specific help. A successful command returns exit code `0`; failure returns `1`.

## Suggested sequence

```powershell
station list modules --config ./workstation
station explain --config ./workstation
station validate --config ./workstation
station check --config ./workstation
station apply --config ./workstation
```

`explain` and the inventory commands may restore declared plugins to discover their exports. `validate`, `check` and `apply` also restore them implicitly. `check` calls each module in check mode; custom modules must honor that mode themselves. `apply` stops when a task fails and reports completed task records.
