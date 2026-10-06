# Lua roles and variables

Station evaluates `role.lua`, `defaults.lua` and `vars/*.lua` through [NLua](https://github.com/NLua/NLua). The root manifest remains `station.yaml`. Choose one task file and one defaults file per role; combining formats for the same artifact is ambiguous. Lua defaults can also accompany a YAML or JavaScript role.

## Variables

A variable file returns exactly one table with string keys:

```lua
-- roles/editor/defaults.lua
return {
    editor_directory = ref('user_home') .. '/.config/editor',
    enabled = true,
    retries = 3,
    extensions = { 'lua', 'yaml' },
    options = {},
    excluded = array()
}
```

`vars/*.lua` follows the same contract. YAML, JavaScript and Lua variable files are merged together in filename order. The global `vars` table exposes references to variables visible at this stage. For example, a later file can return `{ selected_theme = vars.theme }`. Role defaults are evaluated before root overrides, with an empty `vars` table; use `ref('path')` for references there.

String-keyed tables become mappings. Consecutive integer keys starting at 1 become ordered arrays. `{}` is an empty mapping; `array()` is an empty array, and `array('first', 'second')` is also supported. Mixed, sparse, cyclic and excessively nested tables are rejected. Lua `nil` removes a table entry, so it does not represent an explicit null element. Numbers, booleans and UTF-8 strings retain their types; functions and userdata cannot be configuration values.

## Tasks

`role.lua` registers tasks using `task(module, parameters)` or `task(name, module, parameters)`. Parameters must be a mapping. The role file does not return values.

```lua
-- roles/editor/role.lua
task('Render editor settings', modules.template, {
    template = 'editor/settings.json',
    destination = vars.editor_directory .. '/settings.json'
})

task(modules.copy, {
    file = 'editor/keybindings.json',
    destination = vars.editor_directory .. '/keybindings.json'
})
```

`modules` contains the built-in module names `copy`, `template`, `script`, `winget`, `scoop` and `apt`. Custom modules use their short or fully qualified name as a string. Task registration order determines execution order; ordinary Lua loops and functions can generate tasks.

`vars.editor_directory`, nested references such as `vars.editor.theme`, and `ref('editor.theme')` preserve variable references in Station's object model. Concatenation with `..` or `tostring` produces Scriban markup, resolved in the task's final variable scope after overrides. These references are placeholders; use Lua literals for arithmetic and branching during configuration generation.

## Runtime

Lua roles provide tasks; place defaults in `defaults.lua`. Description and inline defaults are available through the YAML role format. Each file runs in a fresh Lua state. `require('helper')` can load `helper.lua` beside the current artifact. Helper files in `vars/` are also discovered as variable files, so keep variable helpers in a subdirectory.

`print(...)` writes diagnostics to stderr. Evaluation has an instruction budget of 100,000 and a two-second CPU-time check at hook intervals. These checks apply to Lua instructions; blocking native calls are outside this limit. Lua scripts run with standard libraries and NLua integration privileges, so use trusted configuration files.

See [the example role](https://github.com/2chevskii/station/tree/main/examples/workstation/roles/luaexample) for a complete layout. Select `luaexample` in `station.yaml` to run it.
