# Getting started

## Requirements and build

Install the .NET 10 SDK and clone the [Station repository](https://github.com/2chevskii/station). Build the CLI from the repository root:

```powershell
dotnet build Station.slnx -v minimal
```

The commands below invoke the built CLI directly. For a single executable, see [publishing](./development.md#publishing).

```powershell
dotnet src/Station/bin/Debug/net10.0/Station.dll --help
```

## Create a configuration root

Create this layout in a directory of your choice:

```text
my-station/
├── station.yaml
├── files/
│   └── greeting.txt
└── roles/
    └── greeting/
        └── role.yaml
```

`station.yaml` selects the role:

```yaml
roles:
  - greeting
```

Put any text in `files/greeting.txt`. In `roles/greeting/role.yaml`, declare a task:

```yaml
description: Installs a greeting file.
tasks:
  - name: Copy greeting
    copy:
      file: greeting.txt
      destination: "{{ user_home }}/station-greeting.txt"
```

`user_home` is provided by Station. The `copy` module reads the named artifact and updates the destination only when bytes differ.

## Inspect and run

From the repository root, replace `my-station` with the path to your configuration:

```powershell
dotnet src/Station/bin/Debug/net10.0/Station.dll explain --config my-station
dotnet src/Station/bin/Debug/net10.0/Station.dll validate --config my-station
dotnet src/Station/bin/Debug/net10.0/Station.dll check --config my-station
dotnet src/Station/bin/Debug/net10.0/Station.dll apply --config my-station
```

`explain` shows a static plan. `validate` binds parameters and inspects tasks. `check` evaluates expected changes without writing the file. `apply` writes it. A later `apply` reports no change when the file already matches.

The repository also includes a larger [workstation example](https://github.com/2chevskii/station/tree/master/examples/workstation). Some package tasks there are commented out because they require platform-specific tools.
