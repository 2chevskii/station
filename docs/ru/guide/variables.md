# Переменные и шаблоны

Во внутренней модели Station переменные сохраняют типы. Объекты, списки, числа и логические значения из YAML остаются типизированными до связывания параметров модуля. Переменные окружения и значения CLI добавляются как строки.

## Приоритет значений

Для задачи внутри роли поздние источники переопределяют ранние:

1. `defaults.yaml` или `defaults.js` роли.
2. Встроенное поле `defaults` в объектной форме `role.yaml`.
3. Корневые файлы `vars/*.yaml` и `vars/*.js` в порядке имён без учёта регистра.
4. Поле `vars` в `station.yaml`.
5. Переменные окружения с префиксом `CONFIG_TOOL_VAR_`.
6. Повторяемые параметры CLI `--set key=value`.

Station также добавляет `user_home` из каталога профиля текущего пользователя. Оно объединяется после перечисленных источников, поэтому считайте это имя зарезервированным.

`vars/10-machine.yaml`:

```yaml
git:
  default_branch: main
terminal_theme: Campbell
```

`roles/git/defaults.yaml`:

```yaml
git_config_path: "{{ user_home }}/.gitconfig"
```

Для вложенных ключей переменных окружения и CLI используются точки:

```powershell
station check --config ./workstation --set git.default_branch=develop
```

Для простого имени можно задать переменную окружения `CONFIG_TOOL_VAR_TERMINAL_THEME`. Ключ с точкой, например `CONFIG_TOOL_VAR_GIT.DEFAULT_BRANCH`, также задаёт вложенное значение; способ его установки зависит от оболочки.

## Подстановка Scriban

Строки могут содержать выражения Scriban:

```yaml
tasks:
  - template:
      template: git/config
      destination: "{{ user_home }}/.gitconfig"
```

Файлы шаблонов тоже используют Scriban. Например, `templates/git/config.scriban`:

```text
[init]
    defaultBranch = {{ git.default_branch }}
```

Перед связыванием параметров модуля значения вычисляются в области переменных задачи. Задача роли видит объединение настроек роли и корневых переменных; корневая задача — корневые переменные. Посмотреть видимые значения позволяет `station explain --config <каталог>`.
