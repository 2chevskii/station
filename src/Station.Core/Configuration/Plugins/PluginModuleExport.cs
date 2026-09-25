namespace Station.Core.Configuration.Plugins;

public sealed record PluginModuleExport(
    string AssemblyPath,
    string? TypeName,
    string? ExpectedName
);
