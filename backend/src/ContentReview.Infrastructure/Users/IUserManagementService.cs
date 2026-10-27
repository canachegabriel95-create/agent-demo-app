namespace ContentReview.Infrastructure.Users;

/// <summary>What administrators may change about author accounts.</summary>
public interface IUserManagementService
{
    /// <summary>
    /// Changes an author's display name and biography. Email, password, role and status are not touched. Works for
    /// Active and Suspended authors. A change is recorded in the audit history (field names only, not the text).
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or the user is an administrator.</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The name or the biography is invalid.</exception>
    /// <exception cref="Domain.Exceptions.ConflictException"><paramref name="version"/> is no longer current.</exception>
    Task<UserDetails> UpdateProfileAsync(
        Guid id, string? fullName, string? bio, uint version, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends an author: status, reason and timestamp are saved, every session of the author is revoked and the
    /// event is audited, all in one transaction. The author's articles, submissions and reviews are not touched.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id.</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The id belongs to an administrator, or the reason is not 10-500 characters.</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">Stale <paramref name="version"/>, or already suspended.</exception>
    Task<UserDetails> SuspendAsync(
        Guid id, string? reason, uint version, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a suspended author Active again. Sessions stay revoked, so the author has to sign in again.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id.</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The id belongs to an administrator.</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">Stale <paramref name="version"/>, or not suspended.</exception>
    Task<UserDetails> ReactivateAsync(
        Guid id, uint version, Guid actorUserId, CancellationToken cancellationToken = default);
}
