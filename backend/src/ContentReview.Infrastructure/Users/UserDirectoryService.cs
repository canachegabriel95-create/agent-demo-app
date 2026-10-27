using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Users;

public sealed class UserDirectoryService(ContentReviewDbContext db) : IUserDirectoryService
{
    private const string LikeEscape = "\\";

    public async Task<PagedResult<UserListItem>> ListAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        var users = db.Users.AsNoTracking().Where(u => u.Role == UserRole.Author);

        var term = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{EscapeLike(term)}%";
            users = users.Where(u =>
                EF.Functions.ILike(u.FullName, pattern, LikeEscape)
                || EF.Functions.ILike(u.NormalizedEmail, pattern, LikeEscape));
        }

        if (filter.Status is { } status)
        {
            users = users.Where(u => u.Status == status);
        }

        var totalCount = await users.CountAsync(cancellationToken);

        var ordered = (filter.Sort, filter.Direction) switch
        {
            (UserSort.FullName, SortDirection.Desc) => users.OrderByDescending(u => u.FullName.ToLower()),
            (UserSort.FullName, _) => users.OrderBy(u => u.FullName.ToLower()),
            (_, SortDirection.Asc) => users.OrderBy(u => u.CreatedAt),
            _ => users.OrderByDescending(u => u.CreatedAt),
        };

        // The id breaks ties, so a page boundary never repeats or skips a row.
        var items = await ordered
            .ThenBy(u => u.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(u => new UserListItem(u.Id, u.FullName, u.Email, u.Status, u.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserListItem>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<UserDetails> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
                       .FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Author, cancellationToken)
                   ?? throw new NotFoundException();

        // One grouped query for all four counts.
        var grouped = await db.Articles.AsNoTracking()
            .Where(a => a.AuthorId == id)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(ArticleStatus status) => grouped.FirstOrDefault(g => g.Status == status)?.Count ?? 0;

        return new UserDetails(
            user.Id,
            user.FullName,
            user.Email,
            user.Bio,
            user.Role,
            user.Status,
            user.CreatedAt,
            user.SuspendedAt,
            user.SuspensionReason,
            new ArticleCounts(
                CountOf(ArticleStatus.Draft),
                CountOf(ArticleStatus.PendingReview),
                CountOf(ArticleStatus.Published),
                CountOf(ArticleStatus.Rejected)),
            user.Version);
    }

    /// <summary>Makes <c>\</c>, <c>%</c> and <c>_</c> literal in a LIKE pattern that uses <c>\</c> as its escape.</summary>
    public static string EscapeLike(string value) =>
        value.Replace(LikeEscape, LikeEscape + LikeEscape).Replace("%", LikeEscape + "%").Replace("_", LikeEscape + "_");
}
