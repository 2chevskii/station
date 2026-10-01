# JavaScript roles and variables

Station can load role tasks from `role.js` and variables from `defaults.js` or `vars/*.js`. JavaScript is evaluated by the embedded Jint runtime. `station.yaml` remains YAML. Use one role file format and one defaults format per role; `role.yaml` and `role.js` together are ambiguous.

## Define variables

A JavaScript variable file exports a top-level object as its default export:

```js
// roles/editor/defaults.js
export default {
  editor_directory: 'C:/Users/example/.config/editor',
  editor_theme: 'dark',
}
```

Root `vars/*.js` files follow the same contract. They are loaded in filename order alongside YAML files. A later file can use variables already visible at its point in the merge.

## Register tasks

`role.js` calls the global `task` function. Use `task(module, parameters)` or `task(name, module, parameters)`. Parameters must be an object.

```js
// roles/editor/role.js
import { copy, template } from 'station:modules/builtin'
import { editor_directory } from 'station:vars'

task('Create editor settings', template, {
  template: 'editor/settings.json',
  destination: `${editor_directory}/settings.json`,
})

task(copy, {
  file: 'editor/keybindings.json',
  destination: `${editor_directory}/keybindings.json`,
})
```

`station:modules/builtin` exports `copy`, `template`, `script`, `winget`, `scoop`, and `apt` as module names. `station:vars` exports references to visible variables with valid JavaScript identifiers and a default object for all root keys. These references become Scriban expressions when converted to strings, so the eventual values are resolved in the task's variable scope. A nested variable can be referenced through the default export:

```js
import vars from 'station:vars'

task('Example', 'template', {
  template: 'example',
  destination: `${vars.paths.config}/example.txt`,
})
```

JavaScript role files register tasks; they do not provide YAML's `description` or inline `defaults` fields. Put defaults in `defaults.js`. The runtime limits evaluation time, statement count and memory, so keep these files focused on configuration generation.
