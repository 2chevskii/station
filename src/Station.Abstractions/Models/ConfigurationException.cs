namespace Station.Abstractions.Models;

public sealed class ConfigurationException(string message) : Exception(message);
