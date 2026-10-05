# CLI reference

The commands below use `station` as shorthand for the CLI. The published Windows file is `Station.exe`; during development, use `dotnet src/Station/bin/Debug/net10.0/Station.dll` from the repository root.

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
| `--set key=value` | Override a variable. Repeat the option for multiple values; the last value for a key wins. Nested keys use dots, for example `--set git.user=Alex`. Values enter as strings, including empty values and quoted whitespace. |
| `--plugin-cache-dir <path>` | Override the plugin cache root. Relative paths resolve from the configuration root. |
| `--output text\|json` | Result format; defaults to `text`. JSON results are written to stdout and progress messages to stderr. |

Options can appear before or after the command. `--config`, `--plugin-cache-dir` and `--output` can each appear once. Every `--set` occurrence needs at least one `key=value` argument.

Use `station <command> --help` for command-specific help. A successful command returns exit code `0`; failure returns `1`; cooperative cancellation returns `130`. A successful `check` returns `0` even when it reports pending changes.

## JSON results

All seven commands support `--output json`:

```powershell
station check --config ./workstation --output json > check.json
station --output json list modules --config ./workstation
```

Each completed invocation produces one JSON document on stdout, including failed configuration, task and plugin operations. Progress messages go to stderr. Managed `Console.Out` and `Console.Error` writes made by modules during execution also go to stderr. Custom modules should use those writers for diagnostics; native code that bypasses them must keep the stdout stream reserved for Station's result.

The common envelope is:

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Integer `1`. Breaking format changes require a new version; readers should tolerate additional fields. |
| `command` | Resolved command, such as `check`, `restore` or `list modules`. An invalid root invocation uses `station`. |
| `succeeded` | Whether the operation succeeded. |
| `exitCode` | The same numeric result returned by the process: `0`, `1` or `130`. |
| `message` | Human-readable summary. |
| `diagnostics` | Array of diagnostic strings, including warnings on successful runs. |
| `data` | Command-specific object, or null for argument errors, cancellation and unexpected invocation errors. |

The `data` object depends on the command:

| Command | Data |
| --- | --- |
| `validate`, `check`, `apply` | `summary` with `taskCount`, `changedCount`, `failedCount`, `skippedCount`; and a `tasks` array in execution order. |
| `restore` | `plugins` with `requirement`, `pluginName`, `version`, `pluginRoot`, `cacheHit`, `isLocal`. Successfully restored plugins remain present when another requirement fails. |
| `list roles`, `list modules` | `components` with `name`, nullable `description` and `source`. |
| `explain` | Nullable `plan`, containing `text`, `kind` and recursive `children`. Kinds are `root`, `section`, `task`, `detail`, `value` and `error`. |

Each task contains `taskName`, `moduleName`, nullable `roleName`, `status`, `changed`, `failed`, `skipped`, nullable `message`, and nullable object `outputs`. Status is `failed`, `changed`, `skipped` or `ok`, in that precedence order. The separate flags preserve partial outcomes: a failed task can also have `changed: true`.

Task records and summary counts describe execution attempts. `validate` returns an empty `tasks` array because it only inspects tasks. Resolution, binding and inspection failures appear in diagnostics without an execution record. Earlier task records are retained when a later task fails. Cancellation returns `data: null`; already applied changes remain, and forced process termination can prevent a result from being written.

Argument errors use JSON when a valid `--output json` selection is available. Invalid output-format values use the normal parser error output. `--help` and `--version` keep their standard text output.

## Suggested sequence

```powershell
station list modules --config ./workstation
station explain --config ./workstation
station validate --config ./workstation
station check --config ./workstation
station apply --config ./workstation
```

`explain` and the inventory commands may restore declared plugins to discover their exports. `validate`, `check` and `apply` also restore them implicitly. `check` calls each module in check mode; custom modules must honor that mode themselves.

`validate` collects task errors and continues inspecting the remaining tasks without executing them. `check` and `apply` stop at the first module resolution, parameter binding, inspection, privilege or execution failure. A task that fails inspection is never executed. The result retains warnings and records from earlier tasks. Changes already made by `apply` remain in place; execution is sequential and has no rollback.
