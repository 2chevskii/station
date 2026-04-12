namespace ConfigTool.Abstractions;

public sealed class ConfigurationException(string message) : Exception(message);
