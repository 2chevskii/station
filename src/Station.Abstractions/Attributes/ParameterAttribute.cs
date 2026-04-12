namespace Station.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ParameterAttribute : Attribute
{
    public ParameterAttribute(string? name = null)
    {
        Name = name;
    }

    public string? Name { get; }

    public string[] Aliases { get; init; } = [];

    public bool Required { get; init; }

    public string? Description { get; init; }
}
