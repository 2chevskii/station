using System.Text.Json.Nodes;

namespace Station.Abstractions.Modules;

public sealed record ModuleResult(
    bool Changed,
    bool Failed = false,
    bool Skipped = false,
    string? Message = null,
    JsonObject? Outputs = null
)
{
    public JsonObject OutputsOrEmpty => Outputs ?? [];

    public static ModuleResult NoChange( string? message = null, JsonObject? outputs = null ) =>
        new ModuleResult( false, false, false, message, outputs );

    public static ModuleResult ChangedResult( string? message = null, JsonObject? outputs = null ) =>
        new ModuleResult( true, false, false, message, outputs );

    public static ModuleResult SkippedResult( string message, JsonObject? outputs = null ) =>
        new ModuleResult( false, false, true, message, outputs );

    public static ModuleResult Failure( string message, JsonObject? outputs = null ) =>
        new ModuleResult( false, true, false, message, outputs );
}