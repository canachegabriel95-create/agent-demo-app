using ContentReview.Domain.Common;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Api.Auth;

/// <summary>
/// Checks login credentials. Every failure (invalid email, unknown email, wrong password, suspended user, role that
/// does not match the portal) yields <c>null</c>, and a password hash is verified in every case so the response time
/// does not reveal whether an account exists.
/// </summary>
public sealed class CredentialsValidator(ContentReviewDbContext db, IPasswordHashingService hashing)
{
    // Hash of a random password nobody knows; verified for unknown emails to spend the same effort as for real ones.
    private static string? _decoyHash;

    public async Task<User?> ValidateAsync(
        string email, string password, Portal portal, CancellationToken cancellationToken)
    {
        var normalized = EmailAddress.IsValid(email) ? EmailAddress.Normalize(email) : null;
        var user = normalized is null
            ? null
            : await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        var passwordMatches = hashing.Verify(user?.PasswordHash ?? DecoyHash(), password);

        return user is not null
               && passwordMatches
               && user.Status == UserStatus.Active
               && user.Role == portal.RequiredRole()
            ? user
            : null;
    }

    private string DecoyHash() => _decoyHash ??= hashing.Hash($"{Guid.NewGuid():N}Aa1!{Guid.NewGuid():N}");
}
