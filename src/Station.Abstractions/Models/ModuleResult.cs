using System.Text.Json.Nodes;

namespace Station.Abstractions;

public sealed record ModuleResult(
    bool Changed,
    bool Failed = false,
    bool Skipped = false,
    string? Message = null,
    JsonObject? Outputs = null)
{
    public JsonObject OutputsOrEmpty => Outputs ?? [];

    public static ModuleResult NoChange(string? message = null, JsonObject? outputs = null) =>
        new(false, false, false, message, outputs);

    public static ModuleResult ChangedResult(string? message = null, JsonObject? outputs = null) =>
        new(true, false, false, message, outputs);

    public static ModuleResult SkippedResult(string message, JsonObject? outputs = null) =>
        new(false, false, true, message, outputs);

    public static ModuleResult Failure(string message, JsonObject? outputs = null) =>
        new(false, true, false, message, outputs);
}
