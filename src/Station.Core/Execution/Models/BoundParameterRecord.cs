namespace Station.Core.Execution.Models;

internal sealed record BoundParameterRecord(
    string Name,
    string DisplayValue,
    bool IsDeferred,
    bool IsDefaultValue,
    string? SourceName);
