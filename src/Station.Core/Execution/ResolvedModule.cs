using Station.Abstractions.Interfaces;

namespace Station.Core.Execution;

internal sealed record ResolvedModule(
    string ProviderName,
    string ShortName,
    string FullName,
    IConfigModule Module);
