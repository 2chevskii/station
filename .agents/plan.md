# Config Tool Plan

## Status

- Phase: planning only
- Coding status: paused until plan is agreed
- Last updated: 2026-04-12

## Context

We are designing a local machine configuration tool with Windows-first ergonomics that can manage:

- dotfiles
- application installation
- application settings
- system settings
- the local host OS
- secondary environments such as WSL when a module needs them

The initial inspiration is Ansible:

- declarative state in YAML
- modules with parameters
- templating
- variables

The main motivation for a custom tool is to avoid Ansible's weak Windows ergonomics and the bootstrap friction around WSL and PowerShell Core.

## Current Product Snapshot

The current prototype already has:

- CLI based on `System.CommandLine`
- commands: `validate`, `check`, `apply`
- YAML config loading with profiles and roles
- variable loading and template rendering
- built-in modules in the current prototype:
  - `package`
  - `dotfile`
  - `script`
- plugin loading through `.dll` assemblies
- support for host and WSL task execution in the current prototype

## Working Assumptions

- Windows is the primary control plane
- PowerShell Core should be a first-class runtime, not an afterthought
- the tool should manage the machine from one place
- the plan should be completed before major new implementation begins
- core execution stays strictly local to the machine where the tool runs
- future remote or secondary execution abstractions, if ever needed, should live outside the core

## Goals To Clarify

- What exact problem boundary the tool should own
- Which configuration domains are first-class in v1
- What execution model should be authoritative
- How modules should be authored and distributed
- How state detection, drift, and idempotency should work
- What safety model is required for destructive or privileged changes
- What bootstrap story should exist for a fresh Windows machine
- What the long-term configuration layout should be after simplifying the current root/profile model

## Candidate Capability Areas

### Configuration Model

- root manifest
- root role list
- roles
- reusable task files
- layered variables
- templating
- secrets handling
- conditional execution
- dependencies between tasks or roles

### Execution Model

- validate
- dry-run
- apply
- selective execution by role or tag
- elevated vs non-elevated execution
- restart or reboot handling
- partial failure policy
- parallel vs sequential execution

### Managed Resources

- package managers as separate resource modules
- files and templates
- scripts
- environment variables
- registry
- services
- scheduled tasks
- Windows features
- winget configuration integration
- WSL distributions and packages
- shell profiles
- Git configuration
- SSH configuration
- editor and IDE settings

### Extensibility

- built-in modules
- a separate standard-modules package or project
- baseline modules as separate projects with one project per module
- plugin modules from assemblies
- plugin-provided reusable roles
- script-backed modules
- shared module contract
- versioning and compatibility policy
- avoid forcing cross-environment abstractions into the shared core contract
- plugin directory layouts that can later support non-.NET plugins and module assets

### UX And Operations

- logging and diagnostics
- machine-readable output
- progress reporting
- diff preview
- backup and rollback strategy
- audit trail
- config schema and editor support
- examples and starter templates

## Decisions

- CLI uses `System.CommandLine`
- CLI uses top-level statements instead of a `Program` class
- planning is tracked in `.agents/plan.md`
- task-level explicit `target` should be removed from the core execution model
- the OS running the tool is always the implicit host
- core should think only in terms of the local OS; remote or secondary execution is out of scope for the current architecture
- if remote execution abstractions are needed later, they should live in a separate library rather than in core
- package managers should be represented by separate modules such as `winget`, `scoop`, `apt`, `chocolatey`, not by one generic `package` module
- module parameters are module-specific; we do not want a shared engine-level context model for things like WSL
- core should not ship concrete modules
- baseline modules such as dotfiles, package-manager integrations, and script execution should live in a separate project or package outside core
- core should depend only on abstractions and module discovery/loading mechanisms, not on a built-in module catalog
- the baseline module set should follow the rule `1 project = 1 module`
- module project naming should be explicit, for example `Station.Modules.Scoop`
- `schemaVersion` should be removed from the root config model
- root-level `varFiles` should be removed from the config model
- `profiles/` should be removed from the config model for now
- `station.yaml` should become the current YAML serialization of the root configuration object, but the long-term source model should remain format-agnostic
- the in-memory .NET object model should be the source of truth, and future config formats may include YAML, JSON, TOML, Lua, Python, or JavaScript
- `station.yaml` should directly contain the ordered list of roles to execute
- the role entrypoint file should be `role.yaml` instead of `tasks.yaml`
- the conventional role subdirectories should currently be limited to `files/` and `templates/`
- explicit variable import mechanisms are out of scope for now
- `role.yaml` should be the current YAML serialization of a role object, but role definitions should also remain format-agnostic long term
- `role.yaml` should currently be a root-level list of tasks, not an object with `tasks:`
- variable precedence from lower to higher should be:
  - `roles/<role>/defaults.yaml`
  - `vars/*.yaml`
  - `station.yaml` `vars`
  - environment variables with `CONFIG_TOOL_VAR_`
  - CLI `--set key=value`
- `vars/*.yaml` should be loaded non-recursively in deterministic alphabetical order
- if the plugin source configured by the future design is absent or empty, the tool should simply behave as if no plugins are installed, without warnings by default
- the base local config layout should be `station.yaml` + `vars/*` + `roles/*`
- the root manifest should define the ordered list of roles executed by the tool
- the root manifest top-level schema should currently include:
  - `roles`
  - `plugins`
  - `vars`
  - `tasks`
- `vars` in `station.yaml` should be a mapping/dictionary, not a list
- a local role should define only behavior through an ordered task list
- root-level `tasks` should be supported and must execute before any role tasks
- each task should invoke exactly one module
- task parameters should be materialized according to the agreed variable precedence
- modules should be able to access role-owned assets under `roles/<role>/files/` and `roles/<role>/templates/`
- a local role must not contain plugins or modules
- local modules should be supported from `<root>/modules`
- local module discovery should use the layout `<root>/modules/<ModuleName>/<ModuleName>.dll`
- local modules should be treated as peers to plugin-provided modules in the overall capability model
- a plugin may provide modules and/or roles in any quantity
- plugins may depend on other plugins
- roles may depend on plugins and/or modules
- modules must not depend on other modules or plugins
- if a module requires prerequisite system state, it should be responsible for establishing that prerequisite itself
- only local `.NET` DLL modules are in scope for the initial local-modules design
- role dependencies should be inferred from the modules used by their tasks
- roles should not directly depend on plugins
- if a role requires a module that is not available from loaded local or plugin-provided modules, execution should fail before any work starts
- modules and roles should have fully qualified names derived from the providing plugin namespace plus the short artifact name
- local modules and roles should belong to the synthetic plugin namespace `station.local`
- built-in baseline modules and roles should belong to the synthetic plugin namespace `station.builtin`
- examples of fully qualified module names:
  - `station.builtin.apt`
  - `dvchevskii.plugins.chocolatey`
  - `station.local.node`
- short names such as `apt`, `chocolatey`, or `node` should be allowed only when they are unambiguous
- if multiple providers export the same short module or role name, users must reference the fully qualified name
- role names and module names may overlap because they live in separate namespaces by artifact kind
- every plugin should have a manifest stored at the plugin root
- the plugin manifest file name should be `plugin-manifest.yaml` or `plugin-manifest.yml`
- the initial plugin manifest schema should include:
  - `name`
  - `version`
  - `roles`
  - `modules`
- `name` should represent the plugin namespace/provider identity, for example `example.plugin`
- `roles` should list the exported role names
- every exported role must exist under the plugin's `roles/` directory or plugin loading fails
- `modules` should list the exported module names
- every exported module must exist under the plugin's `modules/` directory or plugin loading fails
- exported role and module names in the plugin manifest should be short names, not fully qualified names
- plugin-exported roles should use the layout `roles/<RoleName>/role.yaml`
- plugin-exported modules should use the layout `modules/<ModuleName>/<ModuleName>.dll`
- if both `plugin-manifest.yaml` and `plugin-manifest.yml` exist in the same plugin root, plugin loading should fail
- artifacts present under `roles/` or `modules/` but not listed in the plugin manifest should be treated as internal and not exported
- plugin version should be treated as a semantic version string for now
- plugin-provided roles should be supported in v1
- plugin roles should be referenceable by short name when unambiguous or by fully qualified name otherwise
- non-local plugins should be restorable into a cache under `<root>/.station/plugin_cache`
- the CLI should provide a `restore` command that resolves and stores declared plugins in the cache
- `validate`, `check`, and `apply` should perform an implicit restore before continuing
- future cache-cleanup commands are expected, but not part of the initial design
- packaging should remain a self-contained executable for now
- module prerequisite bootstrapping should remain an implementation detail of each module, not a core-engine concern
- task `target` should be fully removed from the task model
- task syntax should follow an Ansible-like single-module shape:
  - optional `name`
  - exactly one module key at the task root
  - module parameters nested under that module key
- task `name` should be optional
- default task names for role tasks should be `<role_name>.tasks.<one-based index>`
- default task names for root-level tasks should be `station.tasks.<one-based index>`
- canonical module invocation syntax should be the object form where the module key maps to a parameter object
- future sugar forms for module invocation should be allowed as alternative serializations of the same in-memory parameter object, similar to Ansible-style short forms

## Current Architecture Picture

- Local configuration is intentionally small:
  - `station.yaml`
  - `vars/*.yaml`
  - `roles/*`
- local extension points also exist beside configuration:
  - `modules/*`
- `station.yaml` is the local root entrypoint and orchestrates execution by listing roles in order
- `station.yaml` may also define root-level tasks that run before roles
- local roles are declarative behavior units:
  - they define ordered tasks
  - they consume resolved variables
  - they can use role-owned files and templates
  - they do not embed modules or plugins
- local modules are project-owned executable capabilities loaded from the config root
- tasks are thin bindings from configuration to execution:
  - one task calls exactly one module
  - the module receives resolved parameters after variable layering
  - the module is selected by the task's single module-named property
  - different serialized forms of module arguments may later normalize into the same canonical in-memory task shape
- modules are the only execution primitives:
  - they perform work
  - they are self-contained
  - they do not compose other modules or depend on plugins
  - they are expected to establish their own prerequisites when that belongs to their contract
- plugins are reuse and distribution units above modules:
  - a plugin can ship any number of modules
  - a plugin can also ship reusable roles
  - a plugin can depend on other plugins
  - every plugin has a root manifest that declares the exported roles and modules
  - exported artifact names are short names that become fully qualified through the plugin manifest `name`
- this creates two sources of capabilities:
  - local project-owned roles and modules
  - plugin-provided roles and modules
- this yields a layered model:
  - core = orchestration and contracts
  - module = atomic execution capability
  - role = declarative composition of module invocations
  - plugin = packaging and reuse unit for modules and/or roles
- configuration serialization is intentionally decoupled from the in-memory model:
  - YAML is the first supported authoring format
  - the in-memory object graph remains the true model
  - other formats may be added later without changing the conceptual schema
- capability resolution follows this model:
  - every role and module has a fully qualified name
  - short-name resolution is a convenience only
  - collisions disable short-name resolution but do not make the configuration invalid if the fully qualified names are used

## Open Questions

### Scope

- Should v1 focus on workstation bootstrap only, or also long-term drift management?

### Authoring Model

- Do we want to stay close to Ansible semantics, or intentionally diverge where Windows ergonomics demand it?
- Should modules be purely declarative where possible, with scripts used only as escape hatches?
- How strongly should module naming and semantics mirror existing Ansible modules versus being Windows-native first?
- Should roles remain a flat ordered list in `station.yaml`, or do we eventually want role parameters or conditional inclusion?

### Configuration Model

- Which shorthand task-argument syntaxes should be supported initially, and how aggressively should they mirror Ansible?
- How should role dependencies on plugins and/or modules be declared in configuration?

### Packaging

- Will this be a single self-contained executable, a `dotnet tool`, or something else?
- Should plugin distribution be local-only at first, or do we want a future registry model?
- How should the CLI obtain the baseline module pack: bundled alongside the app, referenced as a project/package dependency, or discovered from a modules directory?

### Plugin System

- How should plugins be declared or discovered if we do not want to rely only on a local `plugins/` folder?
- Which plugin source types do we want to support in v1: local paths, directories, archives, package feeds, git repositories, URLs, or something else?
- Do we want plugin installation to be an explicit lifecycle step separate from regular config execution?
- Beyond `name`, `version`, `roles`, and `modules`, what extra plugin manifest metadata will we eventually need?
- Should plugin resolution be fully explicit in config, or should there also be conventional discovery locations?
- How should plugin version pinning and updates work?
- Should plugins be allowed to provide reusable roles in addition to modules?
- If plugins can provide roles, how are those roles referenced from `station.yaml` and how do their variables surface to users?
- What is the trust and review model for plugin-provided roles, given that they package executable behavior at a higher abstraction level than modules?
- How should plugin-to-plugin dependencies be versioned, resolved, and locked?
- How should locally supplied modules and plugin-provided modules coexist in discovery, resolution, and diagnostics?
- How should the restore cache represent multiple resolved versions of the same plugin source over time?
- Should `restore` ever refresh floating references automatically, or only on explicit user intent?
- The current plugin-provider design draft supports these provider kinds:
  - local
  - git
  - url
  - nuget
  - npm
- plugin declarations should identify plugin locations or package coordinates, not the plugin manifest `name`
- plugin identity should come from the downloaded or discovered plugin manifest, not from the declaration syntax
- provider authentication is out of scope for the initial design
- local plugin sources should be restricted to `<root>/plugins`
- local provider should allow directory or archive sources inside `<root>/plugins`
- local provider should not allow escaping outside `<root>/plugins`, but symlinks and hard links inside that directory are allowed
- local provider lookup should search by source name for:
  - `<root>/plugins/<source>/`
  - `<root>/plugins/<source>.zip`
  - `<root>/plugins/<source>.tar`
  - `<root>/plugins/<source>.tar.gz`
  - `<root>/plugins/<source>.tgz`
  - `<root>/plugins/<source>.7z`
- local provider should use:
  - short form: `plugins: [my_plugin]`
  - long form fields:
    - `provider: local`
    - `name: <plugin directory name or archive file name>`
- plugin declarations may explicitly set `provider`, but short forms may infer the provider
- if both `<root>/plugins/<name>/` and an archive candidate for the same short local name exist, the directory should win by default
- users should be able to explicitly reference an archive by file name such as `my_plugin.zip`
- git provider should use:
  - `provider: git`
  - `url: <git repository url>`
  - `ref: <tag|full sha|short sha|branch>`
- if `ref` is omitted for git, the default branch should be used
- in short form, values beginning with `git://` should be interpreted as git sources
- in short form, URLs ending with `.git` should be interpreted as git sources
- in short form, no-extension remote URLs should default to the git provider
- git short form may append `@refname` to specify `ref`
- git authentication is out of scope for now
- url provider should use:
  - `provider: url`
  - `url: <archive url>`
- in short form, remote URLs with recognized archive extensions should be interpreted as url-archive sources
- in short form, remote archive URLs without archive extensions should require explicit `provider: url`
- nuget provider should use:
  - short form: `nuget://Package.Name@version`
  - long form fields:
    - `provider: nuget`
    - `id: <package id>`
    - `version: <version>`
    - `feed: <feed url>`
- if `feed` is omitted for nuget, the default should be `nuget.org`
- if `version` is omitted for nuget, the latest available version should be used
- npm provider should mirror nuget:
  - short form: `npm://@scope/package@version` or `npm://package@version`
  - long form fields:
    - `provider: npm`
    - `id: <package id>`
    - `version: <version>`
    - `registry: <registry url>`
- if `registry` is omitted for npm, the default should be the public npm registry
- if `version` is omitted for npm, the latest available version should be used
- plugin declarations should not support plugin-scoped `vars` for now

### Bootstrap

- What should the zero-to-first-run story look like on a fresh Windows install?
- Is installing PowerShell Core a responsibility of the tool itself in v1?

### Safety

- How explicit should elevation boundaries be?
- Should modules declare whether they are idempotent, previewable, reversible, or reboot-sensitive?
- How far should "module bootstraps its own prerequisites" go before the behavior becomes too implicit or surprising?

## Near-Term Discussion Backlog

- Define the v1 problem statement in one paragraph
- Prioritize managed resource types for v1
- Design bootstrap flow for a clean Windows machine
- Decide how strict the declarative model should be
- Decide how modules declare capabilities and safety properties
- Decide what should be built-in vs plugin-only
- Design the module taxonomy for v1
- Decide how the CLI composes with the baseline module pack while keeping core decoupled
- Define conventions for module project structure, naming, and packaging
- Finalize the simplified config layout after removing profiles and root varFiles
- Define the exact schema for `station.yaml` and `role.yaml`
- Design plugin sourcing, installation, discovery, and manifesting
- Evaluate whether plugins should be able to ship reusable roles as well as modules
- Finalize the exact schema and validation rules for `plugin-manifest.yaml`
- Finalize the plugin declaration schema and provider inference rules in `station.yaml`
- Finalize the root and role task schemas, including default task naming

## Explored Ideas

### Plugin Loading

- Auto-discover plugin assemblies from a local `<root>/plugins` directory
- Discovery patterns considered:
  - `<root>/plugins/*.dll`
  - `<root>/plugins/<plugin_name>/<plugin_name>.dll`
- Motivation captured:
  - keep plugin installation simple
  - leave room for plugin-owned assets
  - prepare for future non-.NET plugin formats
- Status:
  - considered, but no longer treated as the only intended direction
  - plugin loading design is being revisited more broadly

## Change Log

- 2026-04-11: Created initial planning file and captured current scope, assumptions, and open questions.
- 2026-04-11: Decided that the running OS is the implicit host and removed engine-level task targeting from the desired architecture; secondary contexts like WSL should be handled by modules.
- 2026-04-11: Decided that core remains strictly local-only, secondary or remote execution is not part of the current architecture, and package managers should become separate modules rather than one generic package module.
- 2026-04-11: Decided that core should not include concrete modules; baseline modules should be extracted into a separate project or package and composed by the CLI or another outer layer.
- 2026-04-11: Decided that the baseline module set should use one project per module, with explicit names such as `Station.Modules.Scoop`.
- 2026-04-11: Simplified the desired config layout by removing `schemaVersion`, root `varFiles`, and profiles; moving role selection into `station.yaml`; renaming the role entrypoint to `role.yaml`; switching plugins to automatic discovery from `<root>/plugins`; and defining a new variable precedence that ends with CLI `--set`.
- 2026-04-11: Confirmed that `vars/*.yaml` should load non-recursively in alphabetical order, missing plugin sources should be treated as "no plugins" without warnings, and `role.yaml` should stay minimal for now with the former `tasks.yaml` shape.
- 2026-04-11: Reopened the plugin-loading design for broader discussion instead of locking the architecture to `<root>/plugins` discovery only.
- 2026-04-11: Added plugin-provided roles as an explicit design topic for evaluation alongside plugin-provided modules.
- 2026-04-11: Consolidated the current architecture picture: local config stays small, roles remain declarative only, tasks bind one-to-one to modules, modules stay self-contained, and plugins become reuse/distribution units that can provide modules and roles with plugin-to-plugin dependencies.
- 2026-04-11: Added local project-owned modules under `<root>/modules/<ModuleName>/<ModuleName>.dll` to make the local config root structurally symmetrical with plugin-provided capabilities.
- 2026-04-12: Defined collision and naming rules for roles and modules: every artifact has a fully qualified name based on its provider namespace, `station.builtin` and `station.local` are reserved synthetic providers, short names are only convenience aliases when unique, and role/module names may overlap because they are resolved in separate namespaces.
- 2026-04-12: Defined the initial plugin manifest direction: every plugin has a root `plugin-manifest.yaml|yml` containing `name`, `version`, `roles`, and `modules`, and plugin loading must fail if any exported role or module declared there is missing from the plugin payload.
- 2026-04-12: Accepted additional plugin-manifest rules: exported names are short names, plugin roles and modules use explicit directory layouts, dual manifest extensions are an error, unexported artifacts are allowed as internal implementation details, and plugin version is treated as semver for now.
- 2026-04-12: Drafted plugin providers and declaration forms for local sources under `<root>/plugins`, git repositories, remote archive URLs, NuGet feeds, and npm registries, with provider inference in short form and no authentication support yet.
- 2026-04-12: Refined plugin providers: long-form declarations now use provider-specific fields (`name`, `url`, `id`, `version`, `feed`, `registry`) instead of a unified `source`; local directories win over same-named archives; no-extension remote URLs default to git; archive URLs require an archive extension or explicit `provider: url`; plugin-scoped vars were removed from the current design.
- 2026-04-12: Defined the current root and role schema direction: the in-memory .NET model is the source of truth; YAML is only the first serialization; `station.yaml` has top-level `roles`, `plugins`, `vars`, and `tasks`; root tasks run before roles; `role.yaml` is a root-level task list; tasks use an Ansible-like single-module shape; role dependencies are inferred from used modules; plugin-provided roles are supported in v1; non-local plugins use a cache with explicit `restore` and implicit restore during other commands; packaging remains self-contained exe for now.
- 2026-04-12: Confirmed that root `vars` is a mapping/dictionary, root task default names should use `station.tasks.<index>`, and future Ansible-like shorthand forms for module arguments should normalize into the same canonical in-memory task object.
