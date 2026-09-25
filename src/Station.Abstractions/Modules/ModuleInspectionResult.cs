namespace Station.Abstractions.Modules;

public sealed record ModuleInspectionResult(
    PrivilegeRequirement RequiredPrivilege,
    IReadOnlyList<ModuleDiagnostic> Diagnostics
)
{
    public static ModuleInspectionResult Success(
        PrivilegeRequirement requiredPrivilege = PrivilegeRequirement.User
    ) =>
        new ModuleInspectionResult( requiredPrivilege, Array.Empty<ModuleDiagnostic>() );

    public static ModuleInspectionResult Failure( params ModuleDiagnostic[] diagnostics ) =>
        new ModuleInspectionResult( PrivilegeRequirement.User, diagnostics );

    public bool IsValid =>
        Diagnostics.All( static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error );
}