namespace ConfigTool.Abstractions;

public sealed record ModuleDiagnostic(string Message, DiagnosticSeverity Severity = DiagnosticSeverity.Error);
