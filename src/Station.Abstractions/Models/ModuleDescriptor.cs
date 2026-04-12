namespace Station.Abstractions.Models;

public sealed record ModuleDescriptor(string Name, string Version, string? Description = null);
