using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Entities;

public class Invitation
{
    public const int CodeMaxLength = 64;
    public const int MinValidityDays = 1;
    public const int MaxValidityDays = 30;
    public const int DefaultValidityDays = 7;

    private Invitation()
    {
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public Guid? UsedByUserId { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public static Invitation Create(
        string code,
        string email,
        Guid createdByUserId,
        DateTimeOffset now,
        int validityDays = DefaultValidityDays)
    {
        if (validityDays is < MinValidityDays or > MaxValidityDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validityDays),
                validityDays,
                $"Validity must be between {MinValidityDays} and {MaxValidityDays} days.");
        }

        var (trimmedEmail, normalizedEmail) = EmailAddress.Parse(email, nameof(email));
        var created = Guard.Utc(now);

        return new Invitation
        {
            Id = Guid.CreateVersion7(),
            Code = Guard.Text(code, nameof(code), 1, CodeMaxLength),
            Email = trimmedEmail,
            NormalizedEmail = normalizedEmail,
            CreatedByUserId = Guard.NotEmpty(createdByUserId, nameof(createdByUserId)),
            CreatedAt = created,
            ExpiresAt = created.AddDays(validityDays),
        };
    }

    /// <summary>
    /// Used and Revoked win over Expired; Expired is derived (not used, not revoked, <c>ExpiresAt &lt;= now</c>).
    /// </summary>
    public InvitationStatus GetStatus(DateTimeOffset now)
    {
        if (UsedAt is not null)
        {
            return InvitationStatus.Used;
        }

        if (RevokedAt is not null)
        {
            return InvitationStatus.Revoked;
        }

        return ExpiresAt <= now ? InvitationStatus.Expired : InvitationStatus.Available;
    }
}
