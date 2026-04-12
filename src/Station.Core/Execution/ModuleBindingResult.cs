using Station.Abstractions.Enums;
using Station.Abstractions.Models;

namespace Station.Core.Execution;

internal sealed record ModuleBindingResult(
    ConfigModule? Module,
    IReadOnlyList<ModuleDiagnostic> Diagnostics,
    IReadOnlyList<BoundParameterRecord> Parameters,
    bool IsDeferred)
{
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
