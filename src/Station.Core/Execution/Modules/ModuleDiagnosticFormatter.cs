namespace Station.Core.Execution.Modules;

internal static class ModuleDiagnosticFormatter
{
    public static string Format( ModuleDiagnostic diagnostic )
    {
        var prefix = diagnostic.Severity switch
        {
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            var _ => "error",
        };

        return $"{prefix}: {diagnostic.Message}";
    }
}