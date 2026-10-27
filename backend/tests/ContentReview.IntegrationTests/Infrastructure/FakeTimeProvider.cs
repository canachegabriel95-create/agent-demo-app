namespace ContentReview.IntegrationTests.Infrastructure;

/// <summary>A clock tests can move forward, to exercise session expiry without waiting.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
