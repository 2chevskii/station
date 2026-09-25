using Station.Abstractions.Enums;

namespace Station.Abstractions.Models;

public sealed record ModuleDiagnostic(
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error
);
