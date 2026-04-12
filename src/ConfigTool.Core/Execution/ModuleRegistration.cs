using ConfigTool.Abstractions;

namespace ConfigTool.Core;

public sealed record ModuleRegistration(
    string ProviderName,
    string ShortName,
    IConfigModule Module);
