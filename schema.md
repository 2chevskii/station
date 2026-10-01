# Station execution flow

See the [English guide](docs/guide/development.md) or [Russian guide](docs/ru/guide/development.md) for the current architecture and commands.

```mermaid
flowchart TD
    A[CLI command] --> B[Read station.yaml and merge root variables]
    B --> C[Parse plugin requirements]
    C --> D[Restore declared plugins]
    D --> E[Discover built-in, local, and plugin modules and roles]
    E --> F[Resolve selected roles]
    F --> G[Load root tasks, then selected role tasks]
    G --> H{Command}
    H -->|explain| I[Build a static task tree]
    H -->|validate| J[Bind and inspect each task]
    H -->|check| K[For each task: bind, inspect, execute in check mode]
    H -->|apply| L[For each task: bind, inspect, execute in apply mode]
    K --> M[Report expected changes]
    L --> N[Report changes or stop on failure]
```

`restore` ends after plugin restoration. `list roles` and `list modules` restore plugins and report available components without loading selected role tasks.
