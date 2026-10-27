using ContentReview.Domain.Entities;

namespace ContentReview.UnitTests.Domain;

public class UserSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private static UserSession NewSession() => UserSession.Create(Guid.NewGuid(), "hash", Now, Now.AddHours(8));

    [Fact]
    public void A_new_session_is_valid_until_it_expires()
    {
        var session = NewSession();

        Assert.True(session.IsValid(Now));
        Assert.True(session.IsValid(Now.AddHours(8).AddTicks(-1)));
        Assert.False(session.IsValid(Now.AddHours(8)));
        Assert.False(session.IsValid(Now.AddHours(9)));
    }

    [Fact]
    public void A_revoked_session_is_not_valid()
    {
        var session = NewSession();

        session.Revoke(Now.AddMinutes(5));

        Assert.False(session.IsValid(Now.AddMinutes(6)));
        Assert.Equal(Now.AddMinutes(5), session.RevokedAt);
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_revocation_time()
    {
        var session = NewSession();
        session.Revoke(Now.AddMinutes(5));

        session.Revoke(Now.AddMinutes(30));

        Assert.Equal(Now.AddMinutes(5), session.RevokedAt);
    }

    [Fact]
    public void Last_seen_is_updated_at_most_once_per_interval()
    {
        var session = NewSession();
        var interval = TimeSpan.FromMinutes(1);

        Assert.True(session.TryMarkSeen(Now, interval));
        Assert.False(session.TryMarkSeen(Now.AddSeconds(59), interval));
        Assert.Equal(Now, session.LastSeenAt);
        Assert.True(session.TryMarkSeen(Now.AddSeconds(60), interval));
        Assert.Equal(Now.AddSeconds(60), session.LastSeenAt);
    }
}
