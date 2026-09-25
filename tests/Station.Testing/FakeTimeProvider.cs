namespace Station.Testing;

public sealed class FakeTimeProvider( DateTimeOffset utcNow ) : TimeProvider
{
    private DateTimeOffset current = utcNow;

    public override DateTimeOffset GetUtcNow() => current;

    public void Advance( TimeSpan delta ) => current = current.Add( delta );
}