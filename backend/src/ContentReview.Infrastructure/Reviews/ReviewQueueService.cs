using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Persistence;
using ContentReview.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Reviews;

public sealed class ReviewQueueService(ContentReviewDbContext db) : IReviewQueueService
{
    public const int LookupLimit = 20;
    public const string SuspendedReason = "Author account is suspended.";

    public async Task<PagedResult<ReviewQueueItem>> ListQueueAsync(
        ReviewQueueFilter filter, CancellationToken cancellationToken = default)
    {
        var pending = db.Articles.AsNoTracking().Where(a => a.Status == ArticleStatus.PendingReview);
        if (filter.AuthorId is { } authorId)
        {
            pending = pending.Where(a => a.AuthorId == authorId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            pending = pending.Where(a => a.CategoryId == categoryId);
        }

        var totalCount = await pending.CountAsync(cancellationToken);

        var ordered = filter.Direction == SortDirection.Desc
            ? pending.OrderByDescending(a => a.SubmittedAt)
            : pending.OrderBy(a => a.SubmittedAt);

        // The id breaks ties, so a page boundary never repeats or skips a row.
        var rows = await (
                from a in ordered.ThenBy(a => a.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
                join u in db.Users.AsNoTracking() on a.AuthorId equals u.Id
                join c in db.Categories.AsNoTracking() on a.CategoryId equals c.Id
                select new { a, u, c })
            .ToListAsync(cancellationToken);

        // Joining after the page was cut keeps the order; restore it explicitly because a join does not promise it.
        var items = (filter.Direction == SortDirection.Desc
                ? rows.OrderByDescending(r => r.a.SubmittedAt).ThenBy(r => r.a.Id)
                : rows.OrderBy(r => r.a.SubmittedAt).ThenBy(r => r.a.Id))
            .Select(r => new ReviewQueueItem(
                r.a.Id,
                r.a.Title,
                new ArticleCategory(r.c.Id, r.c.Name),
                new QueueAuthor(r.u.Id, r.u.FullName, r.u.Status),
                r.a.SubmittedAt!.Value,
                r.a.SubmissionVersion))
            .ToList();

        return new PagedResult<ReviewQueueItem>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<ReviewArticleDetails> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var article = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                      ?? throw new NotFoundException();

        // What the author submitted is what the administrator reviews, whatever the draft says now.
        var snapshot = article.SubmissionVersion == 0
            ? null
            : await db.ArticleSubmissions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ArticleId == id && s.Version == article.SubmissionVersion, cancellationToken);
        if (snapshot is null)
        {
            throw new NotFoundException();
        }

        var author = await db.Users.AsNoTracking().FirstAsync(u => u.Id == article.AuthorId, cancellationToken);
        var category = await db.Categories.AsNoTracking()
            .Where(c => c.Id == snapshot.CategoryId)
            .Select(c => new ArticleCategory(c.Id, c.Name))
            .FirstOrDefaultAsync(cancellationToken);

        var history = await (
                from r in db.Reviews.AsNoTracking()
                where r.ArticleId == id
                join u in db.Users.AsNoTracking() on r.ReviewerId equals u.Id
                orderby r.SubmissionVersion descending, r.DecidedAt descending
                select new ReviewHistoryEntry(
                    r.SubmissionVersion, r.Decision, r.Reason, r.DecidedAt, new ReviewerRef(u.Id, u.FullName)))
            .ToListAsync(cancellationToken);

        var blocked = article.Status != ArticleStatus.PendingReview
            ? $"Article is not pending review (status {article.Status})."
            : author.Status != UserStatus.Active
                ? SuspendedReason
                : null;

        return new ReviewArticleDetails(
            article.Id,
            snapshot.Title,
            snapshot.Summary,
            snapshot.Body,
            category,
            article.Status,
            article.SubmissionVersion,
            snapshot.SubmittedAt,
            article.PublishedAt,
            new ReviewArticleAuthor(author.Id, author.FullName, author.Email, author.Status),
            history,
            blocked is null,
            blocked,
            article.Version);
    }

    public async Task<IReadOnlyList<AuthorLookupItem>> LookupAuthorsAsync(
        string? search, CancellationToken cancellationToken = default)
    {
        var authors = db.Users.AsNoTracking().Where(u => u.Role == UserRole.Author);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{UserDirectoryService.EscapeLike(term)}%";
            authors = authors.Where(u =>
                EF.Functions.ILike(u.FullName, pattern, "\\") || EF.Functions.ILike(u.NormalizedEmail, pattern, "\\"));
        }

        return await authors
            .OrderBy(u => u.FullName.ToLower())
            .ThenBy(u => u.Id)
            .Take(LookupLimit)
            .Select(u => new AuthorLookupItem(u.Id, u.FullName))
            .ToListAsync(cancellationToken);
    }
}
