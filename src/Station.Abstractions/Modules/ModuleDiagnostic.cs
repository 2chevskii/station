namespace Station.Abstractions.Modules;

public sealed record ModuleDiagnostic(
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error
);