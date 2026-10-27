using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Users;

/// <summary>An author's own account details. Email and role are shown but never changed here.</summary>
public sealed record AuthorProfile(
    Guid Id, string FullName, string Email, string? Bio, UserRole Role, DateTimeOffset RegisteredAt, uint Version);

/// <summary>
/// What an author may do with their own profile. The user id always comes from the session, never from a request,
/// so there is no way to name another author.
/// </summary>
public interface IAuthorProfileService
{
    /// <exception cref="Domain.Exceptions.NotFoundException">The id is not an author's.</exception>
    Task<AuthorProfile> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Changes the display name and the biography (an empty biography clears it).</summary>
    /// <exception cref="Domain.Exceptions.ValidationException">The name or the biography is invalid.</exception>
    /// <exception cref="Domain.Exceptions.ConflictException"><paramref name="version"/> is no longer current.</exception>
    Task<AuthorProfile> UpdateAsync(
        Guid userId, string? fullName, string? bio, uint version, CancellationToken cancellationToken = default);
}
