using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Invitations;

public sealed class InvitationRedemptionService(ContentReviewDbContext db, TimeProvider timeProvider)
    : IInvitationRedemptionService
{
    public Task<InvitationValidationResult> ValidateAsync(
        string? code, string? email, CancellationToken cancellationToken = default) =>
        EvaluateAsync(InvitationCode.Normalize(code), Normalize(email), timeProvider.GetUtcNow(), cancellationToken);

    public async Task<Guid> ConsumeAsync(string? code, string? email, Guid newUserId, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An invitation must be consumed inside the registration transaction. Call Database.BeginTransactionAsync first.");
        }

        var normalizedCode = InvitationCode.Normalize(code);
        var normalizedEmail = Normalize(email);
        var now = timeProvider.GetUtcNow();

        if (normalizedCode is not null && normalizedEmail is not null)
        {
            // One statement decides: a concurrent call waits for the row lock, then sees used_at set and updates nothing.
            var affected = await db.Invitations
                .Where(i => i.Code == normalizedCode
                            && i.NormalizedEmail == normalizedEmail
                            && i.UsedAt == null
                            && i.RevokedAt == null
                            && i.ExpiresAt > now)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(i => i.UsedAt, now).SetProperty(i => i.UsedByUserId, newUserId),
                    cancellationToken);
            if (affected == 1)
            {
                return await db.Invitations
                    .Where(i => i.UsedByUserId == newUserId)
                    .Select(i => i.Id)
                    .SingleAsync(cancellationToken);
            }
        }

        var reason = await EvaluateAsync(normalizedCode, normalizedEmail, now, cancellationToken);
        if (reason == InvitationValidationResult.Valid)
        {
            throw new InvalidOperationException("The invitation could not be consumed, yet nothing prevents its use.");
        }

        throw new InvitationNotRedeemableException(reason);
    }

    private static string? Normalize(string? email) => string.IsNullOrWhiteSpace(email) ? null : EmailAddress.Normalize(email);

    /// <summary>
    /// The email is checked before the status so that a wrong email learns nothing about who used or revoked the code.
    /// Among the statuses Used wins over Revoked, which wins over Expired, as in <c>Invitation.GetStatus</c>.
    /// </summary>
    private async Task<InvitationValidationResult> EvaluateAsync(
        string? normalizedCode, string? normalizedEmail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (normalizedCode is null)
        {
            return InvitationValidationResult.NotFound;
        }

        var invitation = await db.Invitations
            .AsNoTracking()
            .Where(i => i.Code == normalizedCode)
            .Select(i => new { i.NormalizedEmail, i.UsedAt, i.RevokedAt, i.ExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);

        return invitation switch
        {
            null => InvitationValidationResult.NotFound,
            _ when invitation.NormalizedEmail != normalizedEmail => InvitationValidationResult.EmailMismatch,
            { UsedAt: not null } => InvitationValidationResult.AlreadyUsed,
            { RevokedAt: not null } => InvitationValidationResult.Revoked,
            _ when invitation.ExpiresAt <= now => InvitationValidationResult.Expired,
            _ => InvitationValidationResult.Valid,
        };
    }
}
