using Station.Abstractions.Configuration;

namespace Station.Configuration.Abstractions.Documents;

public sealed record RoleDocumentProperties(ConfigObject Defaults, string? Description);
