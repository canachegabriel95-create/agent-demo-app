using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentReview.Infrastructure.Sessions;

public sealed class SessionService(
    ContentReviewDbContext db,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider) : ISessionService
{
    private const int TokenBytes = 32;

    /// <summary>A base64url token of 32 bytes is 43 characters; anything much longer cannot be ours.</summary>
    private const int MaxTokenLength = 128;

    private static readonly TimeSpan LastSeenInterval = TimeSpan.FromMinutes(1);

    public async Task<IssuedSession> CreateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddHours(options.Value.SessionLifetimeHours);

        var session = UserSession.Create(userId, HashToken(token), now, expiresAt);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return new IssuedSession(session.Id, token, session.ExpiresAt);
    }

    public async Task<ValidatedSession?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return null;
        }

        var tokenHash = HashToken(token);
        var found = await db.UserSessions
            .Where(s => s.TokenHash == tokenHash)
            .Join(db.Users, s => s.UserId, u => u.Id, (session, user) => new { session, user })
            .FirstOrDefaultAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (found is null || !found.session.IsValid(now) || found.user.Status != UserStatus.Active)
        {
            return null;
        }

        if (found.session.TryMarkSeen(now, LastSeenInterval))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var user = found.user;
        return new ValidatedSession(found.session.Id, user.Id, user.Email, user.FullName, user.Role, user.Status);
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, now), cancellationToken);
    }

    /// <summary>Lowercase hex SHA-256 of the token; the only form that is stored.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
