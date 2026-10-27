using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Users;

public enum UserSort
{
    RegisteredAt,
    FullName,
}

public enum SortDirection
{
    Asc,
    Desc,
}

public sealed record UserListItem(Guid Id, string FullName, string Email, UserStatus Status, DateTimeOffset RegisteredAt);

public sealed record ArticleCounts(int Draft, int PendingReview, int Published, int Rejected);

public sealed record UserDetails(
    Guid Id,
    string FullName,
    string Email,
    string? Bio,
    UserRole Role,
    UserStatus Status,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason,
    ArticleCounts ArticleCounts,
    uint Version);

public sealed record UserFilter(
    string? Search, UserStatus? Status, UserSort Sort, SortDirection Direction, int Page, int PageSize);

/// <summary>What administrators see of author accounts. Administrators are never part of the directory.</summary>
public interface IUserDirectoryService
{
    /// <summary>
    /// Authors only. <c>Search</c> matches any part of the full name or the email, ignoring case; <c>%</c>, <c>_</c> and
    /// <c>\</c> in it are ordinary characters.
    /// </summary>
    Task<PagedResult<UserListItem>> ListAsync(UserFilter filter, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or the user is an administrator.</exception>
    Task<UserDetails> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
