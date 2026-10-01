# Быстрый старт

## Требования и сборка

Установите .NET 10 SDK и клонируйте [репозиторий Station](https://github.com/2chevskii/station). Из корня репозитория соберите CLI:

```powershell
dotnet build Station.slnx -v minimal
```

Ниже команды вызывают собранный CLI напрямую. Сборка отдельного исполняемого файла описана в разделе [Публикация](./development.md#публикация).

```powershell
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll --help
```

## Создайте корень конфигурации

Создайте в удобном каталоге такую структуру:

```text
my-station/
├── station.yaml
├── files/
│   └── greeting.txt
└── roles/
    └── greeting/
        └── role.yaml
```

В `station.yaml` выберите роль:

```yaml
roles:
  - greeting
```

Запишите любой текст в `files/greeting.txt`. В `roles/greeting/role.yaml` объявите задачу:

```yaml
description: Устанавливает файл с приветствием.
tasks:
  - name: Скопировать приветствие
    copy:
      file: greeting.txt
      destination: "{{ user_home }}/station-greeting.txt"
```

Переменную `user_home` предоставляет Station. Модуль `copy` читает указанный файл и обновляет назначение, только если содержимое отличается.

## Проверьте и примените

Из корня репозитория замените `my-station` на путь к своей конфигурации:

```powershell
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll explain --config my-station
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll validate --config my-station
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll check --config my-station
dotnet src/Station.Cli/bin/Debug/net10.0/Station.Cli.dll apply --config my-station
```

`explain` показывает статический план. `validate` связывает параметры и проверяет задачи. `check` вычисляет ожидаемые изменения без записи файла. `apply` записывает его. Повторный `apply` сообщает, что изменения не нужны, если содержимое уже совпадает.

В репозитории есть и более полный [пример настройки рабочего места](https://github.com/2chevskii/station/tree/master/examples/workstation). Часть задач установки пакетов там закомментирована, поскольку требует утилит конкретной платформы.
