using Station.Abstractions;

namespace Station.Core;

public sealed record ModuleRegistration(
    string ProviderName,
    string ShortName,
    IConfigModule Module);
