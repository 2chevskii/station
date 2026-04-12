namespace Station.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleAttribute : Attribute
{
    public ModuleAttribute(string name, string? description = null)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string? Description { get; }
}
