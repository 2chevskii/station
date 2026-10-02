# Разработка проекта

## Структура репозитория

Station использует .NET 10. Проекты разделяют контракты, загрузку конфигурации, выполнение, восстановление плагинов и встроенные модули:

| Проект | Ответственность |
| --- | --- |
| `Station.Abstractions` | Контракты модулей, переменных и файлов. |
| `Station.Configuration.Abstractions` | Контракты документов конфигурации и значений. |
| `Station.Configuration.Yaml` | Загрузка YAML и подстановка Scriban. |
| `Station.Configuration.JavaScript` | Загрузка переменных и ролей через Jint. |
| `Station.Core` | Обнаружение, связывание, планирование и выполнение задач. |
| `Station.Plugins.Abstractions` | Контракты требований и манифестов плагинов. |
| `Station.Plugins` | Восстановление через отдельных поставщиков и кеширование. |
| `Station.Utilities` | Общие средства запуска процессов и проверки прав. |
| `Station.Modules.*` | Встроенные модули. |
| `Station.Cli` | Точка входа CLI и вывод. |

Модель задачи похожа на Ansible: необязательное имя, один ключ модуля и вложенный объект параметров. YAML — формат сериализации; источником истины служит модель объектов .NET в памяти.

## Внедрение зависимостей

`Station.Cli` регистрирует сервисы через `StationServices.AddStation`, создаёт и освобождает контейнер в `Program.cs`. Сервисы Core и плагинов получают зависимости через конструкторы. `AddStationCore` и `AddStationPlugins` регистрируют внутренние сервисы своих проектов.

Загрузчики конфигурации, провайдеры плагинов, шаблонизатор, средство запуска процессов, проверка прав, HTTP-клиент и источник времени используются совместно. Для каждой задачи `ActivatorUtilities` создаёт отдельный модуль с новым `ModuleContext`, получая остальные зависимости конструктора из DI. Интеграционные тесты используют регистрации CLI и могут подменять отдельные зависимости перед созданием контейнера.

## Сборка и тесты

Из корня репозитория:

```powershell
dotnet build Station.slnx -v minimal
dotnet test --project tests/Station.UnitTests/Station.UnitTests.csproj -v minimal
dotnet test --project tests/Station.IntegrationTests/Station.IntegrationTests.csproj -v minimal
```

Выполняйте сборку и тесты последовательно: проекты разделяют выходные каталоги и состояние восстановления пакетов. В репозитории выбран [Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/tools/dotnet-test-mtp), поэтому в примерах используется `dotnet test --project`. Интеграционные тесты используют временные каталоги и локальные фикстуры; реальные пакетные менеджеры на компьютере не нужны.

Проверка CLI:

```powershell
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll --help
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll explain --config examples/workstation
```

## Публикация

Сборка автономного исполняемого файла для Windows:

```powershell
dotnet publish src/Station.Cli/Station.Cli.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:UseAppHost=true
```

Результат — `Station.Cli.exe` в каталоге `src/Station.Cli/bin/Release/net10.0/win-x64/publish/`.

## Сайт документации

Сайт использует VitePress. Установите Node.js и npm, затем из корня репозитория:

```powershell
npm ci
npm run docs:dev
```

Сборка для публикации и локальный просмотр:

```powershell
npm run docs:build
npm run docs:preview
```

Английские страницы находятся в `docs/guide/`, соответствующие русские — в `docs/ru/guide/`. Навигация и локальный поиск настроены в `docs/.vitepress/config.mts`. При изменении описания поведения обновляйте обе версии.
