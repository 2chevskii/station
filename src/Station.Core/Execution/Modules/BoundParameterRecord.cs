namespace Station.Core.Execution.Modules;

internal sealed record BoundParameterRecord(
    string Name,
    string DisplayValue,
    bool IsDeferred,
    bool IsDefaultValue,
    string? SourceName
);
