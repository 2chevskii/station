using Station.Abstractions.Interfaces;

namespace Station.Core.Execution;

public sealed record ModuleRegistration(
    string ProviderName,
    string ShortName,
    IConfigModule Module);
