using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Entities;

public class User
{
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 100;
    public const int BioMaxLength = 1000;
    public const int PasswordHashMaxLength = 512;

    private User()
    {
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string? Bio { get; private set; }
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>), exposed to clients as <c>version</c>.</summary>
    public uint Version { get; private set; }

    public static User Create(
        string email,
        string passwordHash,
        string fullName,
        UserRole role,
        DateTimeOffset now,
        string? bio = null)
    {
        var (trimmedEmail, normalizedEmail) = EmailAddress.Parse(email, nameof(email));
        var timestamp = Guard.Utc(now);

        return new User
        {
            Id = Guid.CreateVersion7(),
            Email = trimmedEmail,
            NormalizedEmail = normalizedEmail,
            PasswordHash = Guard.Text(passwordHash, nameof(passwordHash), 1, PasswordHashMaxLength),
            FullName = Guard.Text(fullName, nameof(fullName), FullNameMinLength, FullNameMaxLength),
            Bio = Guard.OptionalText(bio, nameof(bio), BioMaxLength),
            Role = Guard.Defined(role, nameof(role)),
            Status = UserStatus.Active,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }
}
