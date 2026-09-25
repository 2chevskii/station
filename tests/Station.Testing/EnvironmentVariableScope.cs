namespace Station.Testing;

public sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string name;
    private readonly string? originalValue;

    public EnvironmentVariableScope( string name, string? value )
    {
        this.name = name;
        originalValue = Environment.GetEnvironmentVariable( name );
        Environment.SetEnvironmentVariable( name, value );
    }

    public void Dispose() => Environment.SetEnvironmentVariable( name, originalValue );
}