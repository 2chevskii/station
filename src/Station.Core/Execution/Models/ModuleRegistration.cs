namespace Station.Core.Execution.Models;

public sealed record ModuleRegistration(
    string ProviderName,
    Type ModuleType);
