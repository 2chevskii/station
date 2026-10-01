# Built-in modules

Built-in modules have the prefix `station.builtin`. The short names below work unless another available module exports the same name. Parameters are nested under the module key. Required parameters are marked **required**.

## `copy`

Copies bytes from a configuration artifact into a local file. The module compares bytes before writing, creates parent directories as needed, and can back up an existing destination. In check mode it only reports whether a write would be needed.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `file` | string, **required** | Logical name under `files/`. |
| `destination` | string, **required** | Destination path. |
| `backup` | boolean, default `true` | Save the previous file as `.bak.<UTC timestamp>` when changing it. |

```yaml
- copy:
    file: git/.gitignore_global
    destination: "{{ user_home }}/.gitignore_global"
```

## `template`

Renders a Scriban artifact and writes UTF-8 without a byte-order mark. It compares rendered bytes and has the same destination and backup behavior as `copy`.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `template` | string, **required** | Logical name under `templates/`, without `.scriban` or `.sbn`. |
| `destination` | string, **required** | Destination path. |
| `backup` | boolean, default `true` | Back up an existing file before changing it. |

```yaml
- template:
    template: git/config
    destination: "{{ user_home }}/.gitconfig"
```

Role artifacts override root artifacts with the same logical name. `copy` and `template` report an administrator privilege requirement when their destination requires elevation.

## `script`

Runs a script under the task's working directory and expects one JSON object on standard output. The default runner is `pwsh`, and Station passes `-NoLogo -NoProfile -File <script-path> <request-json-path>`. It also sets `CONFIG_TOOL_REQUEST_PATH` to the request path. A nonzero exit code or invalid JSON fails the task. The script must implement check behavior itself.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `path` | string, **required** | Script path within the task root. |
| `runner` | string, default `pwsh` | Executable on `PATH` that accepts the PowerShell-style arguments above. |
| `requiresAdmin` | boolean, default `false` | Request elevated privileges during inspection. |

The input JSON contains `mode` (`check` or `apply`), `taskName`, `roleName`, and `variables`. The output JSON can contain `changed`, `failed`, `skipped`, `message`, and `outputs`:

```powershell
$request = Get-Content -LiteralPath $args[0] -Raw | ConvertFrom-Json
$wouldChange = $true
@{
    changed = $wouldChange
    failed = $false
    message = if ($request.mode -eq 'check') { 'Would change' } else { 'Changed' }
} | ConvertTo-Json -Compress
```

This sample only demonstrates the protocol; a real script should inspect the target and make changes only when `mode` is `apply`.

## `winget`

Manages a package through Windows Package Manager. Inspection requires the command on `PATH`. A `machine` scope requests elevation.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `id` | string, **required** | Exact package identifier. |
| `state` | string, default `present` | `present` installs; `absent` uninstalls. |
| `version` | string, optional | Requested install version. |
| `scope` | string, optional | Passed to `winget install --scope`; `machine` requests elevation. |
| `command` | string, default `winget` | Executable name or path. |

```yaml
- winget:
    id: Git.Git
    state: present
```

## `scoop`

Manages a Scoop package. Inspection requires the command on `PATH`. Global installation requests elevation.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `name` | string, **required** | Package name. |
| `state` | string, default `present` | `present` installs; `absent` uninstalls. |
| `global` | boolean, default `false` | Pass `--global` when installing or uninstalling. |
| `command` | string, default `scoop` | Executable name or path. |

```yaml
- scoop:
    name: ripgrep
    state: present
```

## `apt`

Manages a package with `apt-get` on Linux or inside a named WSL distribution on Windows. Inspection checks for `apt-get` locally or `wsl` and the distribution on Windows.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `name` | string, **required** | Package name. |
| `state` | string, default `present` | `present` installs; `absent` removes. |
| `version` | string, optional | Requested package version. |
| `distribution` | string | Required on Windows; WSL distribution name. |
| `sudo` | boolean, default `true` | Prefix package changes with `sudo`. |
| `wslCommand` | string, default `wsl` | WSL executable. |
| `aptGetCommand` | string, default `apt-get` | Package manager executable. |

```yaml
- apt:
    name: ripgrep
    state: present
    distribution: Ubuntu
```

The `distribution` field is needed for this example on Windows. Omit it when running directly on Linux.
