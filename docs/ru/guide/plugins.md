# Плагины

Плагины распространяют роли и модули .NET. Объявите источники в `station.yaml` и выполните `station restore --config <каталог>` для явного восстановления. Команды `list`, `explain`, `validate`, `check` и `apply` при необходимости восстанавливают заявленные плагины автоматически.

## Источники

Поддерживаются пять поставщиков:

| Поставщик | Короткая форма | Объектная форма |
| --- | --- | --- |
| Локальный каталог или архив в `<корень>/plugins/` | `my-plugin` или `my-plugin.zip` | `{ provider: local, name: my-plugin }` |
| Репозиторий Git | `git://github.com/org/repo.git@v1.0.0` | `{ provider: git, url: 'https://example.com/repo.git', ref: main }` |
| Удалённый архив | `https://example.com/plugin.zip` | `{ provider: url, url: 'https://example.com/plugin.tar.gz' }` |
| Пакет NuGet | `nuget://Company.Station.Plugin@1.2.3` | `{ provider: nuget, id: Company.Station.Plugin, version: 1.2.3 }` |
| Пакет npm | `npm://@scope/station-plugin@1.2.3` | `{ provider: npm, id: '@scope/station-plugin', version: 1.2.3 }` |

В YAML это элементы списка:

```yaml
plugins:
  - my-plugin
  - provider: git
    url: https://github.com/example/station-plugin.git
    ref: v1.0.0
  - provider: nuget
    id: Company.Station.Plugin
    version: 1.2.3
    feed: https://api.nuget.org/v3/index.json
  - provider: npm
    id: "@scope/station-plugin"
    version: 1.2.3
    registry: https://registry.npmjs.org/
```

`feed` и `registry` — необязательные параметры соответствующих поставщиков. Для удалённого источника без распознаваемого расширения архива или Git-адреса явно укажите `provider`. Локальный каталог используется на месте; архивы и удалённые источники кешируются.

## Манифест и экспорты

В корне плагина должен быть ровно один `manifest.yaml` или `manifest.yml`. Идентификатор плагина задаёт манифест, а не строка требования.

```yaml
name: example.tools
version: 1.0.0
roles:
  - workstation
modules:
  - assemblies/Example.Plugin.dll:Example.Plugin.CleanupModule
  - assemblies/Example.Plugin.dll:Example.Plugin.SetupModule
```

Экспортируемая роль находится в `roles/workstation/role.yaml` или `role.js`. Запись модуля содержит путь к сборке относительно корня плагина и необязательное полное имя публичного типа `ConfigModule`. Явные имена типов позволяют экспортировать несколько модулей из одной сборки. Старая короткая запись, например `cleanup`, ожидает `modules/cleanup/cleanup.dll` с ровно одним публичным типом модуля. Короткое имя модуля задаёт атрибут `[Module]`. Экспортируются только роли и модули из манифеста.

В примере роль получает имя `example.tools.workstation`, а модули — имена `example.tools.*`. Если короткое имя неоднозначно, укажите полное. Пространства имён ролей и модулей независимы.

## Расположение кеша

По умолчанию корень кеша — `<корень-конфигурации>/.station/plugin_cache`. Содержимое плагина хранится внутри `<manifest.name>/<manifest.version>/`. Источники переопределения корня кеша перечислены от меньшего приоритета к большему:

1. `vars.plugin_cache_dir` в конфигурации.
2. `CONFIG_TOOL_VAR_PLUGIN_CACHE_DIR` в окружении.
3. `--set plugin_cache_dir=<путь>`.
4. `--plugin-cache-dir <путь>`.

Относительные пути отсчитываются от корня конфигурации. Локальные каталоги плагинов остаются по исходному пути.
