# Роли и переменные на JavaScript

Station может читать задачи роли из `role.js`, а переменные — из `defaults.js` или `vars/*.js`. JavaScript выполняется встроенным движком Jint. Корневой `station.yaml` остаётся YAML. Для роли выберите один формат задач и один формат значений по умолчанию; одновременное наличие `role.yaml` и `role.js` неоднозначно.

## Объявите переменные

Файл переменных JavaScript экспортирует корневой объект как `default`:

```js
// roles/editor/defaults.js
export default {
  editor_directory: 'C:/Users/example/.config/editor',
  editor_theme: 'dark',
}
```

Корневые файлы `vars/*.js` используют тот же контракт. Они загружаются в порядке имён вместе с файлами YAML. Следующий файл может использовать переменные, уже доступные на данном этапе объединения.

## Зарегистрируйте задачи

`role.js` вызывает глобальную функцию `task`. Допустимы `task(module, parameters)` и `task(name, module, parameters)`. Параметры должны быть объектом.

```js
// roles/editor/role.js
import { copy, template } from 'station:modules/builtin'
import { editor_directory } from 'station:vars'

task('Создать настройки редактора', template, {
  template: 'editor/settings.json',
  destination: `${editor_directory}/settings.json`,
})

task(copy, {
  file: 'editor/keybindings.json',
  destination: `${editor_directory}/keybindings.json`,
})
```

`station:modules/builtin` экспортирует имена модулей `copy`, `template`, `script`, `winget`, `scoop` и `apt`. `station:vars` экспортирует ссылки на видимые переменные с допустимыми JavaScript-именами и объект `default` со всеми корневыми ключами. При преобразовании в строку ссылки становятся выражениями Scriban, поэтому итоговые значения разрешаются в области переменных задачи. Вложенную переменную можно взять из экспорта `default`:

```js
import vars from 'station:vars'

task('Пример', 'template', {
  template: 'example',
  destination: `${vars.paths.config}/example.txt`,
})
```

JavaScript-файл роли регистрирует задачи; полей `description` и встроенного `defaults`, как у YAML-роли, в нём нет. Значения по умолчанию поместите в `defaults.js`. Движок ограничивает время, количество инструкций и память, поэтому оставляйте эти файлы компактными.
