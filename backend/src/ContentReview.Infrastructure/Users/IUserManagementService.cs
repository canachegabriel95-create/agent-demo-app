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
}
