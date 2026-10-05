# Собственные модули .NET

Модуль — публичный неабстрактный класс, наследующий `ConfigModule` из `Station.Abstractions`. Добавьте классу `[Module]`, объявите один публичный конструктор с `ModuleContext` и пометьте записываемые свойства параметров атрибутом `[Parameter]`. Атрибут задаёт короткое имя модуля; имена свойств становятся camelCase, если имя параметра явно не указано.

Дополнительные параметры конструктора разрешаются через сервисы, зарегистрированные в `Station.Cli`. Каждая задача получает новый экземпляр модуля и свой `ModuleContext`. Пользовательские зависимости регистрируются при настройке DI приложения; загрузка манифеста плагина не регистрирует сервисы.

Модули, запускающие команды, могут получать `IProcessRunner` из `Station.Abstractions.Processes` через конструктор. Передавайте токен отмены задачи в `RunAsync`, чтобы общий исполнитель Station завершал процессы при отмене. Передавайте `OperationCanceledException` вызывающему коду, если токен задачи отменён.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Station.Abstractions.Execution;
using Station.Abstractions.Modules;

namespace Example.Modules;

[Module("ensureDirectory", "Creates a local directory.")]
public sealed class EnsureDirectoryModule : ConfigModule
{
    public EnsureDirectoryModule(ModuleContext context)
        : base(context)
    {
    }

    [Parameter(Required = true)]
    public string Path { get; set; } = string.Empty;

    public override ValueTask<ModuleInspectionResult> InspectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!System.IO.Path.IsPathFullyQualified(Path))
        {
            return ValueTask.FromResult(
                ModuleInspectionResult.Failure(
                    new ModuleDiagnostic("The path must be absolute.")));
        }

        return ValueTask.FromResult(ModuleInspectionResult.Success());
    }

    public override ValueTask<ModuleResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(Path))
        {
            return ValueTask.FromResult(
                ModuleResult.NoChange($"Directory '{Path}' already exists."));
        }

        if (Context.Mode == ExecutionMode.Check)
        {
            return ValueTask.FromResult(
                ModuleResult.ChangedResult($"Directory '{Path}' would be created."));
        }

        Directory.CreateDirectory(Path);
        return ValueTask.FromResult(
            ModuleResult.ChangedResult($"Directory '{Path}' was created."));
    }
}
```

Добавьте в проект модуля ссылку на `src/Station.Abstractions/Station.Abstractions.csproj` из репозитория и выберите совместимую версию .NET. Соберите проект и поместите сборку по пути:

```text
<корень-конфигурации>/modules/ensureDirectory/ensureDirectory.dll
```

Имена каталога, DLL и `[Module]` должны совпадать без учёта регистра. Полное имя локального модуля в примере — `station.local.ensureDirectory`.

```yaml
tasks:
  - station.local.ensureDirectory:
      path: "{{ user_home }}/.config/example"
```

При проверке задачи вызывается `InspectAsync`: он может вернуть диагностику или требование повышенных прав. `ExecuteAsync` возвращает `ModuleResult`; учитывайте `Context.Mode`, чтобы `check` не изменял компьютер. `ModuleContext` также предоставляет переменные, файлы, корни конфигурации и роли, сведения о правах и источник времени. Контракт модуля плагина тот же; сборка и тип экспортируются в [манифесте плагина](./plugins.md).
