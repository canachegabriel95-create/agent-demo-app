using ContentReview.Domain.Common;

namespace ContentReview.Domain.Entities;

/// <summary>A login session. Only the SHA-256 hash of the opaque bearer token is stored.</summary>
public class UserSession
{
    public const int TokenHashMaxLength = 128;

    private UserSession()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }

    public static UserSession Create(Guid userId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        var created = Guard.Utc(now);
        var expires = Guard.Utc(expiresAt);
        if (expires <= created)
        {
            throw new ArgumentException("expiresAt must be after the creation time.", nameof(expiresAt));
        }

        return new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = Guard.NotEmpty(userId, nameof(userId)),
            TokenHash = Guard.Text(tokenHash, nameof(tokenHash), 1, TokenHashMaxLength),
            CreatedAt = created,
            ExpiresAt = expires,
        };
    }

    /// <summary>True while the session is neither revoked nor expired.</summary>
    public bool IsValid(DateTimeOffset now) => RevokedAt is null && ExpiresAt > Guard.Utc(now);

    /// <summary>Ends the session. Revoking an already revoked session keeps the original time.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= Guard.Utc(now);

    /// <summary>
    /// Records activity, but at most once per <paramref name="minInterval"/> so reads do not turn into writes.
    /// Returns whether <see cref="LastSeenAt"/> changed.
    /// </summary>
    public bool TryMarkSeen(DateTimeOffset now, TimeSpan minInterval)
    {
        var timestamp = Guard.Utc(now);
        if (LastSeenAt is { } last && timestamp - last < minInterval)
        {
            return false;
        }

        LastSeenAt = timestamp;
        return true;
    }
}
