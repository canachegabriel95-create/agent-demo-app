namespace ContentReview.Domain.Security;

/// <summary>
/// The single password hashing abstraction used by the API (login, registration) and by the
/// <c>hash-password</c> CLI, so a hash generated offline is always accepted by the API.
/// </summary>
public interface IPasswordHashingService
{
    /// <summary>Returns a salted Identity V3 hash (PBKDF2) of <paramref name="password"/>.</summary>
    string Hash(string password);

    /// <summary>Returns <c>true</c> when <paramref name="password"/> matches <paramref name="passwordHash"/>.</summary>
    bool Verify(string passwordHash, string password);
}
