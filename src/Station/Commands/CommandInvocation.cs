namespace Station.Commands;

internal sealed record CommandInvocation(
    string ConfigRoot,
    IReadOnlyDictionary<string, string> Variables,
    string? PluginCacheDirectory
);
