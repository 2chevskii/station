using Station.Abstractions;

namespace Station.Core;

internal sealed record ResolvedModule(
    string ProviderName,
    string ShortName,
    string FullName,
    IConfigModule Module);
