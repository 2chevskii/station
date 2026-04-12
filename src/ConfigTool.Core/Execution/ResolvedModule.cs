using ConfigTool.Abstractions;

namespace ConfigTool.Core;

internal sealed record ResolvedModule(
    string ProviderName,
    string ShortName,
    string FullName,
    IConfigModule Module);
