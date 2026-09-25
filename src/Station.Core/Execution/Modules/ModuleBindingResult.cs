namespace Station.Core.Execution.Modules;

internal sealed record ModuleBindingResult(
    ConfigModule? Module,
    IReadOnlyList<ModuleDiagnostic> Diagnostics,
    IReadOnlyList<BoundParameterRecord> Parameters,
    bool IsDeferred
)
{
    public bool HasErrors =>
        Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
