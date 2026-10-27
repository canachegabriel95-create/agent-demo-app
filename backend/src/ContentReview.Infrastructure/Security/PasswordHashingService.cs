using ContentReview.Domain.Entities;
using ContentReview.Domain.Security;
using Microsoft.AspNetCore.Identity;

namespace ContentReview.Infrastructure.Security;

/// <summary>
/// Wraps <see cref="PasswordHasher{TUser}"/> (Identity V3: PBKDF2-HMAC-SHA512, random salt) without
/// using the rest of ASP.NET Identity. The hasher ignores the user argument.
/// </summary>
public sealed class PasswordHashingService : IPasswordHashingService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(null!, password);
    }

    public bool Verify(string passwordHash, string password)
    {
        if (string.IsNullOrEmpty(passwordHash) || password is null)
        {
            return false;
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(null!, passwordHash, password);
            return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
