using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Invitations;

/// <summary>
/// The single way to check and use an invitation code. Registration (F05) validates early for feedback and then
/// consumes the code inside the transaction that creates the account.
/// </summary>
public interface IInvitationRedemptionService
{
    /// <summary>
    /// Checks the code without changing anything. <paramref name="code"/> may be in any case, with or without dashes;
    /// <paramref name="email"/> is compared in its normalized form.
    /// </summary>
    Task<InvitationValidationResult> ValidateAsync(string? code, string? email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the invitation as used by <paramref name="newUserId"/> with one conditional UPDATE, so of any number of
    /// concurrent calls for the same code exactly one succeeds. It must run inside the registration transaction
    /// (<c>Database.BeginTransactionAsync</c> on the same DbContext) so that the invitation and the account are saved
    /// or rolled back together. Returns the id of the invitation that was used.
    /// </summary>
    /// <exception cref="Domain.Exceptions.InvitationNotRedeemableException">The invitation cannot be used; nothing was changed.</exception>
    /// <exception cref="InvalidOperationException">There is no current transaction.</exception>
    Task<Guid> ConsumeAsync(string? code, string? email, Guid newUserId, CancellationToken cancellationToken = default);
}
