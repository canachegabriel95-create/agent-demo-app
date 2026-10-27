using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Sessions;

/// <summary>A newly created session. <see cref="Token"/> is shown to the client once and never stored.</summary>
public sealed record IssuedSession(Guid SessionId, string Token, DateTimeOffset ExpiresAt);

/// <summary>The session and user behind a valid bearer token.</summary>
public sealed record ValidatedSession(
    Guid SessionId,
    Guid UserId,
    string Email,
    string FullName,
    UserRole Role,
    UserStatus Status);

public interface ISessionService
{
    /// <summary>Creates a session for the user and returns the opaque token (32 random bytes, base64url).</summary>
    Task<IssuedSession> CreateAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the session for a token, or <c>null</c> when it is unknown, revoked, expired or its user is not Active.
    /// </summary>
    Task<ValidatedSession?> ValidateAsync(string token, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every open session of a user (used when an account is suspended). Returns how many were revoked.</summary>
    Task<int> RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
