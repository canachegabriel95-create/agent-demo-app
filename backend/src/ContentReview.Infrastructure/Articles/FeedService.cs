using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using ContentReview.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Articles;

public sealed class FeedService(ContentReviewDbContext db) : IFeedService
{
    /// <summary>
    /// Every read starts here. The Published filter is part of the query itself, so no caller can forget it, and the
    /// content is the submission the administrator approved (the article's own text is the same while it is read-only).
    /// Whether the author is suspended does not matter.
    /// </summary>
    private IQueryable<Row> Published() =>
        from article in db.Articles.AsNoTracking()
        where article.Status == ArticleStatus.Published && article.PublishedAt != null
        join review in db.Reviews.AsNoTracking() on article.Id equals review.ArticleId
        where review.Decision == ReviewDecision.Approved
        join submission in db.ArticleSubmissions.AsNoTracking()
            on new { review.ArticleId, Version = review.SubmissionVersion } equals new { submission.ArticleId, submission.Version }
        join author in db.Users.AsNoTracking() on article.AuthorId equals author.Id
        select new Row
        {
            Id = article.Id,
            Title = submission.Title,
            Summary = submission.Summary,
            Body = submission.Body,
            CategoryId = submission.CategoryId,
            AuthorName = author.FullName,
            PublishedAt = article.PublishedAt!.Value,
        };

    public async Task<PagedResult<FeedItem>> ListAsync(FeedFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = Published();

        var term = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{UserDirectoryService.EscapeLike(term)}%";
            rows = rows.Where(r => EF.Functions.ILike(r.Title, pattern, "\\"));
        }

        if (filter.CategoryId is { } categoryId)
        {
            rows = rows.Where(r => r.CategoryId == categoryId);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        // The id breaks ties, so a page boundary never repeats or skips an article.
        var page = await rows
            .OrderByDescending(r => r.PublishedAt)
            .ThenBy(r => r.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var categories = await CategoriesAsync(page.Select(r => r.CategoryId), cancellationToken);
        var items = page
            .Select(r => new FeedItem(r.Id, r.Title, r.Summary, categories.GetValueOrDefault(r.CategoryId), r.AuthorName, r.PublishedAt))
            .ToList();

        return new PagedResult<FeedItem>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<FeedArticle> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await Published().Where(r => r.Id == id).FirstOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException();

        var categories = await CategoriesAsync([row.CategoryId], cancellationToken);
        return new FeedArticle(
            row.Id, row.Title, row.Summary, row.Body, categories.GetValueOrDefault(row.CategoryId), row.AuthorName, row.PublishedAt);
    }

    private async Task<Dictionary<Guid, ArticleCategory>> CategoriesAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Categories.AsNoTracking()
            .Where(c => distinct.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new ArticleCategory(c.Id, c.Name), cancellationToken);
    }

    // A class with initialisers (not a record built by its constructor): the queries above order by its members.
    private sealed class Row
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = null!;
        public string Summary { get; init; } = null!;
        public string Body { get; init; } = null!;
        public Guid CategoryId { get; init; }
        public string AuthorName { get; init; } = null!;
        public DateTimeOffset PublishedAt { get; init; }
    }
}
