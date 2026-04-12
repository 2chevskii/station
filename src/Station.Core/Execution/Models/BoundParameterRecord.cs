namespace Station.Core.Execution;

internal sealed record BoundParameterRecord(
    string Name,
    string DisplayValue,
    bool IsDeferred,
    bool IsDefaultValue,
    string? SourceName);
