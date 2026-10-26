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
}
