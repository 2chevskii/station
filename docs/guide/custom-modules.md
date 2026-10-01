# Custom .NET modules

Modules are public, non-abstract classes derived from `ConfigModule` in `Station.Abstractions`. Mark the class with `[Module]`, expose one public constructor that accepts `ModuleContext`, and mark writable parameter properties with `[Parameter]`. The attribute provides the module's short name; property names become camelCase unless a parameter name is specified explicitly.

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

Reference the repository's `src/Station.Abstractions/Station.Abstractions.csproj` from your module project and target a compatible .NET runtime. Build the assembly and place it at:

```text
<config-root>/modules/ensureDirectory/ensureDirectory.dll
```

The directory and DLL base names must match the `[Module]` name, ignoring case. A local module's full name is `station.local.ensureDirectory`.

```yaml
tasks:
  - station.local.ensureDirectory:
      path: "{{ user_home }}/.config/example"
```

`InspectAsync` runs when Station validates a task and can report diagnostics or required privileges. `ExecuteAsync` returns a `ModuleResult`; respect `Context.Mode` so `check` does not mutate the machine. `ModuleContext` also exposes variables, artifacts, the configuration and role roots, elevation state, and a time provider. Plugin modules use the same contract; export their assembly and type in the [plugin manifest](./plugins.md#manifest-and-exports).
