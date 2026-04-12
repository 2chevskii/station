# JavaScript Config Support Plan

Status: implementation-ready
Last updated: 2026-04-12

## Goal

Add support for authoring Station roles and variable files in JavaScript while preserving:

- the current Ansible-like task model
- YAML compatibility
- deterministic variable precedence
- a single canonical in-memory model owned by Station

This plan is intentionally implementation-oriented. It defines the target architecture we will build next.

## Scope

### In scope

- `roles/<role>/role.js`
- `roles/<role>/defaults.js`
- `vars/*.js`
- refactoring config loading so role/defaults/vars loading is language-agnostic
- introducing a Station-owned canonical value model instead of relying on `JsonNode` as the source of truth
- early immutable filesystem discovery for the config root and plugin roots
- JavaScript config support inside plugin-provided roles

### Out of scope for this implementation

- `station.js`
- JavaScript-authored root manifest loading
- JavaScript plugin manifests
- Node.js runtime support
- CommonJS support
- public YAML-specific explicit variable-reference syntax
- plugin-provided config loaders used for bootstrap parsing

## Final Decisions

- YAML remains supported and is not being replaced.
- JavaScript support is additive.
- JavaScript config authoring is ESM-only.
- `Jint` is the JavaScript engine.
- `role.js` is DSL-only via `task(...)`.
- `defaults.js` and `vars/*.js` use `export default { ... }`.
- `station:vars` exposes lazy references, not eager values.
- `VariableReference` is a first-class Station value type.
- Station will move from a `JsonNode`-centric internal model to a Station-owned DOM.
- Strings containing Station template markup are represented explicitly through `TemplateMarkup`.
- `defaults.*` evaluate with an empty visible variable scope.
- `vars/*.js` see only variables accumulated from earlier vars files in precedence order.
- same-base-name files with different supported extensions are a hard error, for example `10-common.yaml` and `10-common.js`
- JavaScript from plugin roles/defaults/vars is trusted by default
- JavaScript config execution remains sandboxed regardless of source:
  - no CLR access
  - no filesystem access
  - no network access
- file discovery becomes an early immutable snapshot layer
- the root manifest remains `station.yaml` only in this implementation
- future YAML semantics should move toward the Ansible-like rule where `"{{ somevar }}"` is a whole-value expression, but that is not part of this implementation

## Architectural Overview

The new loading pipeline will be:

1. Discover the config root into an immutable filesystem snapshot.
2. Load `station.yaml`.
3. Resolve and load root vars files through artifact resolvers and variable loaders.
4. Parse plugin declarations from the loaded root manifest.
5. Restore plugins when needed.
6. Discover each plugin root into the same immutable snapshot model.
7. Build role/module catalogs from local and plugin snapshots.
8. Resolve requested roles.
9. Resolve and load role defaults/task artifacts through artifact resolvers and registries.
10. Build effective variable scopes.
11. Bind and execute tasks using the Station DOM as the canonical model.

The architecture is split into five layers:

- discovery
- resolution
- loading
- composition
- execution

## Discovery Layer

### Purpose

Separate filesystem discovery from config interpretation.

The discovery layer runs early and produces an immutable snapshot of the config root. Later layers read from that snapshot instead of walking the filesystem ad hoc.

### Core types

Suggested types:

- `DiscoveredConfigTree`
- `DiscoveredDirectory`
- `DiscoveredFile`

### `DiscoveredConfigTree`

Suggested fields:

- `RootPath`
- `RawRoot`
- `RootManifest`
- `RolesRoot`
- `VarsRoot`
- `FilesRoot`
- `TemplatesRoot`
- `PluginsRoot`
- `ModulesRoot`
- `LocalRoleDirectories`
- `LocalModuleDirectories`

Important notes:

- the snapshot contains structure and metadata, not eagerly loaded file contents
- unknown files remain present in the raw tree
- the snapshot is point-in-time and immutable for the duration of a run

### Plugin roots

After plugin restore, each restored plugin root gets the same snapshot treatment.

That gives us one discovery model for:

- the local config root
- restored plugin roots

### Immediate benefits

- one source of truth for role discovery
- one source of truth for vars/defaults/role file resolution
- one place to detect ambiguity and missing-file problems
- future ability to serve files/templates/modules from discovered semantic roots rather than rescanning disk

### Initial implementation boundary

This implementation will introduce the discovery snapshot and use it for config artifact resolution.

`ModuleArtifactFactory` may continue to scan `files/` and `templates/` initially if that reduces migration risk, but the snapshot model must be designed so that artifact resolution can move there cleanly afterward.

## Resolution Layer

### Purpose

Decide which concrete file or files represent a logical config artifact.

This layer owns:

- ambiguity detection
- missing-file diagnostics
- precedence ordering
- duplicate-base-name checks in `vars/`

### Core types

- `ConfigArtifactKind`
- `ResolvedConfigArtifact`

### `ConfigArtifactKind`

Values for this implementation:

- `RootVarsEntry`
- `RoleDefaults`
- `RoleTasks`

Future candidates:

- `RootManifest`
- `PluginManifest`

### `ResolvedConfigArtifact`

Suggested fields:

- `Kind`
- `FullPath`
- `DisplayName`
- `OwningRoot`
- `RoleName`
- `LoaderId`

### Resolver service

Suggested service:

- `IConfigArtifactResolver`

Suggested methods:

- `IReadOnlyList<ResolvedConfigArtifact> EnumerateRootVarArtifacts(DiscoveredConfigTree tree)`
- `ResolvedConfigArtifact? ResolveRoleDefaultsArtifact(DiscoveredDirectory roleDirectory, string roleFullName)`
- `ResolvedConfigArtifact ResolveRoleTaskArtifact(DiscoveredDirectory roleDirectory, string roleFullName)`

### Resolution rules

#### Root vars

- load supported files under `vars/`
- support `.yaml` and `.js`
- order files by filename using case-insensitive ordinal ordering
- if two supported files share the same basename and differ only by extension, fail

#### Role defaults

- allow zero or one of `defaults.yaml` / `defaults.js`
- if both exist, fail

#### Role tasks

- require exactly one of `role.yaml` / `role.js`
- if both exist, fail
- if neither exists, fail

#### Plugin roles

- use the exact same defaults/task resolution rules as local roles

## Station DOM

### Purpose

Provide one canonical Station-owned value model for:

- variables
- defaults
- vars files
- task parameters

This replaces `JsonNode` as the internal source of truth.

### Core types

Suggested shape:

- `abstract record ConfigValue`
- `sealed record ConfigNull() : ConfigValue`
- `sealed record ConfigScalar(ConfigScalarKind Kind, object Value) : ConfigValue`
- `sealed record ConfigArray(IReadOnlyList<ConfigValue> Items) : ConfigValue`
- `sealed record ConfigObject(IReadOnlyDictionary<string, ConfigValue> Properties) : ConfigValue`
- `sealed record VariableReference(string Path) : ConfigValue`
- `sealed record TemplateMarkup(string TemplateText) : ConfigValue`

Suggested scalar kinds:

- `String`
- `Boolean`
- `Integer`
- `Float`

Notes:

- `ConfigScalar` values must be restricted to immutable primitive CLR values only
- YAML date/time values should continue to normalize to strings, matching current behavior
- `TemplateMarkup` is for deferred template evaluation, not for arbitrary strings

### Why this is the chosen model

- `VariableReference` becomes explicit and typed
- `TemplateMarkup` becomes explicit and typed
- all supported authoring languages normalize into the same internal shape
- Station semantics are no longer hidden inside JSON helpers
- future languages such as TOML/Lua/Python can target the same DOM directly

### DOM invariants

- `ConfigObject` keys are case-insensitive at the Station layer
- `VariableReference.Path` is stored in normalized dotted form
- plain strings without template markup remain `ConfigScalar(String, ...)`
- only loader/runtime code creates `VariableReference` and `TemplateMarkup`

## Variable Semantics

### Precedence

Root variable precedence remains:

1. `vars/*` in filename order
2. root manifest `vars`
3. environment variables with `CONFIG_TOOL_VAR_`
4. CLI `--set`

Role variable precedence remains:

1. role defaults
2. root variables

### VariableSet

`VariableSet` should become an immutable wrapper over a `ConfigObject`, not over `JsonNode`.

Suggested responsibilities:

- store raw unresolved values
- support overlay creation
- support raw lookup by path
- support resolved lookup by path

Suggested API direction:

- `ConfigObject Root`
- `VariableSet CreateOverlay(ConfigObject? overlay)`
- `bool TryGetRaw(string path, out ConfigValue? value)`
- `bool TryResolve(string path, out ConfigValue? value)`
- `T? GetResolved<T>(string path)` or equivalent conversion API

### Merge semantics

Variable merging must operate on raw DOM values, not resolved values.

Rules:

- object + object merges recursively
- any non-object incoming value replaces the previous value
- `VariableReference` and `TemplateMarkup` are atomic values

This is essential so an early reference can still resolve against a later overlay.

### Variable resolution

Introduce a dedicated resolver service rather than spreading this logic across `VariableSet`.

Suggested service:

- `IConfigValueResolver`

Suggested responsibilities:

- resolve `VariableReference`
- resolve `TemplateMarkup`
- resolve nested arrays/objects recursively
- detect self-reference and indirect cycles
- materialize resolved object graphs for template execution

### Resolution rules

#### `VariableReference`

`VariableReference("foo.bar")` means:

- do not snapshot the current value
- resolve `foo.bar` against the effective `VariableSet` of the current consumer

This preserves type:

- string
- bool
- number
- object
- array
- null

#### `TemplateMarkup`

`TemplateMarkup` means:

- defer template evaluation until consumption time
- evaluate against the effective `VariableSet` of the current consumer

Iteration 1 semantics:

- template markup resolves to a string value
- Station will continue to use Scriban as the rendering engine

Future direction, not part of this implementation:

- distinguish whole-value expressions from mixed string templates so `"{{ somevar }}"` can produce typed values in the Ansible-like way

### Resolution order at task binding time

When binding a raw task parameter:

1. resolve `VariableReference`
2. resolve `TemplateMarkup`
3. convert the resolved `ConfigValue` to the target CLR type

This order applies recursively inside arrays and objects too.

## Loading Layer

### Purpose

Parse/evaluate concrete files and normalize them into the Station DOM.

### Loader registries

Use artifact-specific registries rather than a single global format enum.

Suggested interfaces:

- `IRoleDocumentLoader`
- `IVariableDocumentLoader`

Possible future interfaces:

- `IRootManifestLoader`
- `IPluginManifestLoader`

### `IRoleDocumentLoader`

Suggested members:

- `string Id`
- `IReadOnlyList<string> SupportedFileNames`
- `bool CanLoad(ResolvedConfigArtifact artifact)`
- `ConfigArray LoadRoleTasks(ResolvedConfigArtifact artifact, ConfigEvaluationContext context)`

### `IVariableDocumentLoader`

Suggested members:

- `string Id`
- `IReadOnlyList<string> SupportedFileNames`
- `bool CanLoad(ResolvedConfigArtifact artifact)`
- `ConfigObject LoadVariables(ResolvedConfigArtifact artifact, ConfigEvaluationContext context)`

### `ConfigEvaluationContext`

Suggested fields:

- `ConfigRoot`
- `Artifact`
- `VisibleVariables`
- `RoleName`
- `RoleRoot`
- `HelperRegistry`

This context defines what the loader is allowed to see during evaluation.

### Loader implementations for this work

- `YamlRoleDocumentLoader`
- `YamlVariableDocumentLoader`
- `JavaScriptRoleDocumentLoader`
- `JavaScriptVariableDocumentLoader`

### Shared helpers

Shared internal services should exist for:

- DOM normalization from YAML values
- DOM normalization from Jint values
- template markup detection in strings
- diagnostics with source path context
- JavaScript engine setup

## YAML Loaders

### YAML variables/defaults

Behavior:

- parse YAML to CLR values
- normalize to Station DOM
- detect template-bearing strings and store them as `TemplateMarkup`
- require a top-level object

### YAML roles

Behavior:

- parse YAML to CLR values
- normalize to Station DOM
- require a top-level array
- each task item must normalize to a `ConfigObject`

### YAML references

This implementation does not add a new public YAML reference syntax.

YAML continues to rely on:

- plain values
- template markup in strings

`VariableReference` remains an internal Station concept required primarily for JavaScript authoring in this iteration.

## JavaScript Runtime

### Engine

Use `Jint`.

Every JavaScript artifact evaluation gets:

- a fresh engine instance
- module support enabled
- bounded execution
- no CLR access
- no filesystem access
- no network access

### Safety limits

Configure explicit limits for:

- execution time
- statement count
- memory usage where supported

These limits protect the Station process even though JS cannot mutate the target system directly.

### Synthetic modules and globals

#### `station:modules/builtin`

Exports builtin module names for ergonomic imports.

Recommended shape:

- named exports such as `copy`, `template`, `winget`
- each export can simply be the module name string

That keeps the simplest possible contract:

- `task(copy, { ... })`
- `task("plugin.namespace.module", { ... })`

#### `station:vars`

Purpose:

- expose the variables visible at the current evaluation point

Contract:

- default export is a tree of lazy variable handles
- named exports exist for top-level variables that are valid JS identifiers
- using those handles in exported data or task parameters becomes `VariableReference`

Important semantic rule:

- these are references to variable paths, not snapshots of current values

#### Global `task(...)`

Only available inside `role.js`.

Supported overloads:

- `task(name, moduleRef, parameters)`
- `task(moduleRef, parameters)`

Rules:

- `name` is optional
- `moduleRef` must normalize to a non-empty module name
- `parameters` must be an object
- registration order defines task order

The JS role loader converts the collected registrations into the same logical task shape as YAML.

### JavaScript variable files

`defaults.js` and `vars/*.js` must:

- be ESM modules
- export a default plain object

The loader converts the exported object into the Station DOM:

- primitives -> `ConfigScalar` / `ConfigNull`
- arrays -> `ConfigArray`
- objects -> `ConfigObject`
- variable handles -> `VariableReference`
- strings with Station template markup -> `TemplateMarkup`

Unsupported JS values should fail clearly:

- functions
- symbols
- host objects not recognized by the loader
- cyclic exported structures

### JavaScript role files

`role.js` must:

- be an ESM module
- register tasks via `task(...)`
- not use a default task export

The loader should reject:

- zero valid task registrations when the file intended to define tasks and produced none
- invalid `task(...)` signatures
- parameters that are not plain objects

## Visibility Rules

### Root vars files

When loading `vars/*.js`, `station:vars` exposes only variables accumulated from earlier vars files in filename order.

This gives deterministic composition without snapshotting referenced values.

### Role defaults

When loading `defaults.js`, `station:vars` is empty.

This keeps defaults from having special loader-time access to higher-precedence scopes.

Important nuance:

- deferred `TemplateMarkup` inside defaults may still resolve later when consumed against the effective task scope
- that is acceptable because it is runtime resolution, not loader-time visibility

### Role tasks

When loading `role.js`, `station:vars` exposes the fully composed role variable scope:

- role defaults
- root vars
- environment overrides
- CLI overrides

This matches the effective scope visible to role tasks.

## Composition Layer

### Purpose

Turn discovered artifacts and loaded DOM values into runtime-ready Station configuration objects.

### Main responsibilities

- merge root variable layers
- merge role defaults over root variables
- parse task documents into `LoadedTask`
- build role/module catalogs from discovered roots

### LoadedTask

`LoadedTask` should change so task parameters use the Station DOM instead of `JsonObject`.

Suggested direction:

- `ConfigObject Parameters`

### Task parsing rules

The Ansible-like task model remains unchanged:

- optional `name`
- exactly one module key besides `name`
- module parameters nested under that key

This validation happens after the document loader returns DOM values.

## Execution Layer Changes

### Binder

`ModuleBinder` must stop assuming raw parameters are `JsonNode`.

New flow:

- receive raw `ConfigValue`
- resolve deferred values with `IConfigValueResolver`
- convert the result to the target CLR type

### Template rendering bridge

The template engine still consumes an object graph, so we need a bridge:

- DOM -> resolved object graph for Scriban globals

This bridge should:

- resolve `VariableReference`
- resolve `TemplateMarkup`
- recursively convert `ConfigObject` and `ConfigArray`

### Explain output

Explain should display deferred values intelligibly.

Recommended formatting examples:

- `<ref: foo.bar>`
- rendered scalar values after resolution where appropriate
- clear indication when a value is deferred or unresolved due to a cycle/error

## Root Manifest and Plugin Manifest

### Root manifest

`station.yaml` remains YAML-only in this implementation.

We do not add:

- `station.js`
- root-manifest loader registries

### Plugin manifests

Plugin manifest loading remains YAML-only too.

However, plugin role validation must stop assuming `role.yaml` only and instead use the same artifact resolver rules as local roles.

## Implementation Plan

### Phase 1: discovery-first refactor

- introduce `DiscoveredConfigTree`
- build snapshots for the local config root
- replace ad hoc role/module discovery with snapshot-backed discovery where practical
- introduce `IConfigArtifactResolver`
- keep behavior YAML-only
- add tests for discovery and ambiguity rules

### Phase 2: loader registries with YAML only

- introduce `IRoleDocumentLoader` and `IVariableDocumentLoader`
- implement YAML role and variable loaders
- route current role/defaults/vars loading through resolver + registries
- preserve existing YAML behavior

### Phase 3: Station DOM

- introduce `ConfigValue` and related DOM types
- migrate variable loading to `ConfigObject`
- migrate `VariableSet` away from `JsonNode`
- migrate `LoadedTask.Parameters` away from `JsonObject`
- introduce DOM merge helpers
- introduce DOM-to-object-graph conversion for templates
- keep behavior aligned with current YAML semantics as closely as possible

### Phase 4: DOM-based resolution and binding

- introduce `IConfigValueResolver`
- implement lazy `VariableReference` resolution with cycle detection
- implement `TemplateMarkup` resolution
- update `ModuleBinder`
- update explain formatting
- add tests for deferred values, nesting, and cycles

### Phase 5: JavaScript variable documents

- add `Jint`
- implement JS engine setup with strict limits
- implement `JavaScriptVariableDocumentLoader`
- support `defaults.js`
- support `vars/*.js`
- implement `station:vars`
- add tests for visibility, lazy refs, and mixed YAML/JS precedence

### Phase 6: JavaScript role DSL

- implement `JavaScriptRoleDocumentLoader`
- implement global `task(...)`
- implement `station:modules/builtin`
- normalize JS task registrations into the canonical task DOM
- add tests for task ordering, validation, and mixed YAML/JS role loading

### Phase 7: plugin role validation alignment

- make plugin role validation use the same resolver as local roles
- support plugin roles with `role.js` and `defaults.js`
- add tests for plugin role defaults/task resolution

### Phase 8: optional follow-up

Not required to land initial JavaScript config support, but enabled by this architecture:

- make `ModuleArtifactFactory` consume discovered semantic roots rather than rescanning the filesystem
- move more runtime artifact lookup onto the immutable discovery snapshot

## Testing Plan

### Discovery and resolution

- missing root manifest
- role with no role file
- role with both `role.yaml` and `role.js`
- role with both `defaults.yaml` and `defaults.js`
- vars directory with mixed `.yaml` and `.js`
- vars directory with duplicate basenames across extensions

### YAML regression coverage

- root vars precedence
- role defaults precedence
- root task parsing
- role task parsing
- no behavior regressions for YAML-only configs

### Station DOM

- object merge semantics
- overlay semantics
- scalar preservation
- array preservation
- conversion to template object graph

### Variable resolution

- simple direct reference
- nested object/array references
- references crossing precedence layers
- self-reference cycle
- indirect cycle
- template markup resolution

### JavaScript variable files

- valid default object export
- invalid export type
- visibility of earlier vars
- lazy variable refs from `station:vars`
- strings with template markup
- unsupported JS values

### JavaScript role files

- valid `task(...)` registrations
- unnamed tasks
- invalid module refs
- invalid parameters shape
- order preservation
- visibility of full role scope

### Plugin roles

- plugin role with YAML role file
- plugin role with JS role file
- plugin role with JS defaults file
- plugin role resolution errors

### CLI smoke tests after implementation

- `dotnet build Station.slnx -v minimal`
- `dotnet test tests/Station.Tests/Station.Tests.csproj -v minimal`
- `dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll --help`
- if explain output changes materially:
  - `dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll explain --config examples/workstation`

## Error Handling Expectations

Representative diagnostics:

- `Role 'station.local.demo' contains both 'role.yaml' and 'role.js'.`
- `Role 'station.local.demo' is missing 'role.yaml' or 'role.js'.`
- `Role 'station.local.demo' contains both 'defaults.yaml' and 'defaults.js'.`
- `Vars files '10-common.yaml' and '10-common.js' are ambiguous because they share the same basename.`
- `JavaScript vars file '.../10-common.js' must export a default object.`
- `JavaScript role file '.../role.js' called task(...) with invalid arguments.`
- `Variable reference cycle detected: foo -> bar -> foo.`
- `JavaScript config '.../role.js' exceeded the configured execution limits.`

## Risks and Guardrails

### Main risk

The biggest cost is the move away from `JsonNode`.

Why we still accept it:

- JS support needs first-class deferred references
- template-bearing values also deserve first-class treatment
- a JSON-sentinel approach would be cheaper short term but harder to evolve

### Guardrails

- keep the root manifest YAML-only for this implementation
- keep JavaScript surface area minimal
- keep loader registries artifact-specific
- keep file-resolution policy centralized
- keep JS sandbox identical for local and plugin sources

## Future Work After This Plan

- `station.js`
- whole-value YAML expression semantics closer to Ansible
- shared helpers exposed both to Scriban and JavaScript
- snapshot-backed runtime artifact lookup for files/templates/modules everywhere
- additional authoring languages built on the same DOM and loader-registry architecture

## Implementation Readiness

There are no blocking architectural questions remaining for the scope of this implementation.

The next step is to execute the phases above in order, starting with:

1. discovery-first refactor
2. loader registries with YAML only
3. Station DOM introduction
