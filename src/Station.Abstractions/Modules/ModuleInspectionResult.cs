using Station.Abstractions.Execution;

namespace Station.Abstractions.Modules;

public sealed record ModuleInspectionResult(
    PrivilegeRequirement RequiredPrivilege,
    IReadOnlyList<ModuleDiagnostic> Diagnostics
)
{
    public static ModuleInspectionResult Success(
        PrivilegeRequirement requiredPrivilege = PrivilegeRequirement.User
    ) => new(requiredPrivilege, Array.Empty<ModuleDiagnostic>());

    public static ModuleInspectionResult Failure(params ModuleDiagnostic[] diagnostics) =>
        new(PrivilegeRequirement.User, diagnostics);

    public bool IsValid =>
        Diagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}
