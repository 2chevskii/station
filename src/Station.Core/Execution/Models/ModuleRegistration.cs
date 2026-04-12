namespace Station.Core.Execution;

public sealed record ModuleRegistration(
    string ProviderName,
    Type ModuleType);
