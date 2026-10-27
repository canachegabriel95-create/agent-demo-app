using ContentReview.Domain.Articles;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Articles;

public sealed class MyArticlesService(ContentReviewDbContext db) : IMyArticlesService
{
    public async Task<PagedResult<MyArticleItem>> ListAsync(
        Guid authorId, MyArticlesFilter filter, CancellationToken cancellationToken = default)
    {
        // Always the author's own articles: nothing in the filter can widen this.
        var articles = db.Articles.AsNoTracking().Where(a => a.AuthorId == authorId);

        var term = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{UserDirectoryService.EscapeLike(term)}%";
            articles = articles.Where(a => EF.Functions.ILike(a.Title, pattern, "\\"));
        }

        if (filter.Status is { } status)
        {
            articles = articles.Where(a => a.Status == status);
        }

        if (filter.CategoryId is { } categoryId)
        {
            articles = articles.Where(a => a.CategoryId == categoryId);
        }

        var totalCount = await articles.CountAsync(cancellationToken);

        var desc = filter.Direction == SortDirection.Desc;
        var ordered = filter.Sort switch
        {
            MyArticleSort.Title => desc
                ? articles.OrderByDescending(a => a.Title.ToLower())
                : articles.OrderBy(a => a.Title.ToLower()),
            // A draft has no submission date: those come last in either direction.
            MyArticleSort.SubmittedAt => desc
                ? articles.OrderBy(a => a.SubmittedAt == null).ThenByDescending(a => a.SubmittedAt)
                : articles.OrderBy(a => a.SubmittedAt == null).ThenBy(a => a.SubmittedAt),
            _ => desc ? articles.OrderByDescending(a => a.UpdatedAt) : articles.OrderBy(a => a.UpdatedAt),
        };

        // The id breaks ties, so a page boundary never repeats or skips an article.
        var rows = await ordered
            .ThenBy(a => a.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.CategoryId,
                a.Status,
                a.UpdatedAt,
                a.SubmittedAt,
                a.PublishedAt,
                a.Version,
            })
            .ToListAsync(cancellationToken);

        var categoryIds = rows.Where(r => r.CategoryId is not null).Select(r => r.CategoryId!.Value).Distinct().ToList();
        var categories = await db.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new ArticleCategory(c.Id, c.Name), cancellationToken);

        // Feedback matters while the article is Rejected: the reason of its latest rejection.
        var rejectedIds = rows.Where(r => r.Status == ArticleStatus.Rejected).Select(r => r.Id).ToList();
        var reasons = rejectedIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : (await db.Reviews.AsNoTracking()
                    .Where(r => rejectedIds.Contains(r.ArticleId) && r.Decision == ReviewDecision.Rejected)
                    .Select(r => new { r.ArticleId, r.SubmissionVersion, r.DecidedAt, r.Reason })
                    .ToListAsync(cancellationToken))
                .GroupBy(r => r.ArticleId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => r.SubmissionVersion).ThenByDescending(r => r.DecidedAt).First().Reason);

        var items = rows.Select(r => new MyArticleItem(
                r.Id,
                r.Title,
                r.CategoryId is { } id && categories.TryGetValue(id, out var category) ? category : null,
                r.Status,
                r.UpdatedAt,
                r.SubmittedAt,
                r.PublishedAt,
                r.Status == ArticleStatus.Rejected && reasons.TryGetValue(r.Id, out var reason) ? reason : null,
                ArticleLifecycle.AllowedActions(r.Status, ActorKind.Owner).Select(a => a.ToString().ToLowerInvariant()).ToList(),
                r.Version))
            .ToList();

        return new PagedResult<MyArticleItem>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<ArticleStatusCounts> StatsAsync(Guid authorId, CancellationToken cancellationToken = default)
    {
        var grouped = await db.Articles.AsNoTracking()
            .Where(a => a.AuthorId == authorId)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(ArticleStatus status) => grouped.FirstOrDefault(g => g.Status == status)?.Count ?? 0;

        return new ArticleStatusCounts(
            CountOf(ArticleStatus.Draft),
            CountOf(ArticleStatus.PendingReview),
            CountOf(ArticleStatus.Published),
            CountOf(ArticleStatus.Rejected));
    }
}
