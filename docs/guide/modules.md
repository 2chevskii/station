# Built-in modules

Built-in modules have the prefix `station.builtin`. The short names below work unless another available module exports the same name. Parameters are nested under the module key. Required parameters are marked **required**.

Package modules accept only `present` and `absent` for `state`, ignoring case and surrounding spaces. An explicit empty or null state is invalid. They reject invalid parameters and package query failures before making changes. Check mode reports the same errors as apply mode.

## `copy`

Copies bytes from a configuration artifact into a local file. The module compares bytes before writing, creates parent directories as needed, and can back up an existing destination. In check mode it only reports whether a write would be needed.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `file` | string, **required** | Logical name under `files/`. |
| `destination` | string, **required** | Destination path. |
| `backup` | boolean, default `true` | Save the previous file as `.bak.<UTC timestamp>`, adding `.1`, `.2`, etc. when the name already exists. |

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

Both modules write a complete temporary file next to the actual destination before replacing an existing file or publishing a new one. Existing files keep their Windows access rules or Unix permission bits; backups receive the same permissions. A read-only destination fails without clearing its attributes. The destination directory must permit creation, replacement and cleanup of files.

File symbolic links remain links: Station updates the final target, including creating a missing target. Backups stay beside the configured destination path. Replacement creates a new file identity, so other hard-link names keep the previous file contents.

Cancellation before replacement preserves the existing contents. Failed or cancelled writes clean up their temporary files and unused backups when filesystem permissions allow it. A successful update keeps every existing backup; writes in the same second receive distinct numbered names. These guarantees apply to each file update. `apply` remains sequential without rollback across tasks, and forced process termination can leave staging or recovery files.

## `script`

Runs a script under the task's working directory and expects one JSON object on standard output. The default runner is `pwsh`, and Station passes `-NoLogo -NoProfile -File <script-path> <request-json-path>`. It also sets `CONFIG_TOOL_REQUEST_PATH` to the request path. A nonzero exit code or invalid JSON fails the task. The script must implement check behavior itself.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `path` | string, **required** | Script path within the task root. |
| `runner` | string, default `pwsh` | Executable on `PATH` that accepts the PowerShell-style arguments above. |
| `requiresAdmin` | boolean, default `false` | Request elevated privileges during inspection. |

The input JSON contains `mode` (`check` or `apply`), `taskName`, `roleName`, and `variables`. The output JSON can contain `changed`, `failed`, `skipped`, `message`, and `outputs`:

| Output field | Accepted value | When omitted |
| --- | --- | --- |
| `changed`, `failed`, `skipped` | Boolean; strings, numbers and null are invalid. | `false` |
| `message` | String or null. | null |
| `outputs` | Object or null. | null |

The response must be one JSON object with unique property names, including nested objects. Invalid response types fail the task with a diagnostic. Extra fields are ignored. A script can report both `changed: true` and `failed: true` when it made partial changes before failing.

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

Station queries the exact installed ID and compares the installed version column with `version`. It distinguishes WinGet's no-applications-found exit code from query errors. Unrecognized, truncated or ambiguous table output fails the task. See the [WinGet list contract](https://learn.microsoft.com/windows/package-manager/winget/list).

| Parameter | Type | Meaning |
| --- | --- | --- |
| `id` | string, **required** | Exact package identifier. |
| `state` | string, default `present` | `present` installs; `absent` uninstalls. |
| `version` | string, optional | Requested install version. |
| `scope` | string, optional | `user` or `machine`; filters queries, installation and removal. `machine` requests elevation. |
| `command` | string, default `winget` | Executable name or path. |

```yaml
- winget:
    id: Git.Git
    state: present
```

## `scoop`

Manages a Scoop package. Inspection requires the command on `PATH`. Global installation requests elevation.

Installed packages are read from [`scoop export`](https://github.com/ScoopInstaller/Scoop/blob/master/libexec/scoop-export.ps1). Station matches an exact app name in the requested local or global scope. A similarly named app or an installation in the other scope does not satisfy the task. Invalid inventory output and incomplete installations are reported as errors.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `name` | string, **required** | Package name or `bucket/name`. The bucket selects the installation source; installed identity and removal use the app name. |
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

Station reads both status and version through [`dpkg-query`](https://manpages.debian.org/trixie/dpkg/dpkg-query.1.en.html). Remaining configuration files count as absent. Partially installed packages require installation for `present` or removal for `absent`. Fatal query errors and malformed status output stop the task. Windows execution uses WSL's direct execution mode and reads the distribution list as UTF-16.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `name` | string, **required** | Exact Debian package name, optionally qualified as `name:architecture`. Patterns are invalid. |
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
