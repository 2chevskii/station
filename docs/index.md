---
layout: home

hero:
  name: Station
  text: Configure your workstation from one directory
  tagline: Declare roles, tasks, files and plugins; inspect the plan before applying changes.
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: Configuration reference
      link: /guide/configuration

features:
  - title: Desired state
    details: Repeated runs compare the machine with the declared state and report only needed changes.
  - title: Safe workflow
    details: Restore plugins, explain the plan, validate tasks, and run a check before applying.
  - title: Extensible
    details: Combine built-in modules with local .NET modules and plugins from five source providers.
---

Station is a local .NET command-line tool. Its configuration root contains `station.yaml`, optional role definitions, variables, files, templates, and modules. Start with the [quick guide](/guide/getting-started), then use the [CLI reference](/guide/cli) and [module reference](/guide/modules).
