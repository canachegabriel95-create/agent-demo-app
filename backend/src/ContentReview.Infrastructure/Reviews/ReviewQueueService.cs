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
        var articles =
            from a in db.Articles.AsNoTracking()
            where a.Status == ArticleStatus.PendingReview
            join u in db.Users.AsNoTracking() on a.AuthorId equals u.Id
            join c in db.Categories.AsNoTracking() on a.CategoryId equals c.Id
            select new QueueRow
            {
                ArticleId = a.Id,
                RevisionId = null,
                Kind = ReviewItemKind.Article,
                Title = a.Title,
                CategoryId = c.Id,
                CategoryName = c.Name,
                AuthorId = u.Id,
                AuthorName = u.FullName,
                AuthorStatus = u.Status,
                SubmittedAt = a.SubmittedAt!.Value,
                SubmissionVersion = a.SubmissionVersion,
            };

        var revisions =
            from r in db.ArticleRevisions.AsNoTracking()
            where r.Status == RevisionStatus.PendingReview
            join a in db.Articles.AsNoTracking() on r.ArticleId equals a.Id
            join u in db.Users.AsNoTracking() on a.AuthorId equals u.Id
            join c in db.Categories.AsNoTracking() on r.CategoryId equals c.Id
            select new QueueRow
            {
                ArticleId = a.Id,
                RevisionId = r.Id,
                Kind = ReviewItemKind.Revision,
                Title = r.Title,
                CategoryId = c.Id,
                CategoryName = c.Name,
                AuthorId = u.Id,
                AuthorName = u.FullName,
                AuthorStatus = u.Status,
                SubmittedAt = r.SubmittedAt!.Value,
                SubmissionVersion = r.SubmissionVersion,
            };

        var pending = articles.Concat(revisions);
        if (filter.AuthorId is { } authorId)
        {
            pending = pending.Where(r => r.AuthorId == authorId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            pending = pending.Where(r => r.CategoryId == categoryId);
        }

        var totalCount = await pending.CountAsync(cancellationToken);

        var ordered = filter.Direction == SortDirection.Desc
            ? pending.OrderByDescending(r => r.SubmittedAt)
            : pending.OrderBy(r => r.SubmittedAt);

        // The ids break ties, so a page boundary never repeats or skips a row.
        var rows = await ordered
            .ThenBy(r => r.ArticleId)
            .ThenBy(r => r.RevisionId)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new ReviewQueueItem(
                r.ArticleId,
                r.Title,
                new ArticleCategory(r.CategoryId, r.CategoryName),
                new QueueAuthor(r.AuthorId, r.AuthorName, r.AuthorStatus),
                r.SubmittedAt,
                r.SubmissionVersion,
                r.Kind,
                r.RevisionId))
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

    public async Task<ReviewRevisionDetails> GetRevisionAsync(Guid revisionId, CancellationToken cancellationToken = default)
    {
        var revision = await db.ArticleRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken);
        if (revision is null || revision.SubmissionVersion == 0)
        {
            throw new NotFoundException();
        }

        var article = await db.Articles.AsNoTracking().FirstAsync(a => a.Id == revision.ArticleId, cancellationToken);
        var author = await db.Users.AsNoTracking().FirstAsync(u => u.Id == article.AuthorId, cancellationToken);

        var categoryIds = new[] { revision.CategoryId, article.CategoryId }.Where(id => id is not null).Select(id => id!.Value).ToList();
        var categories = await db.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new ArticleCategory(c.Id, c.Name), cancellationToken);

        var history = await (
                from r in db.RevisionReviews.AsNoTracking()
                where r.RevisionId == revisionId
                join u in db.Users.AsNoTracking() on r.ReviewerId equals u.Id
                orderby r.SubmissionVersion descending, r.DecidedAt descending
                select new RevisionReviewHistoryEntry(
                    r.SubmissionVersion, r.Decision, r.Reason, r.DecidedAt, new ReviewerRef(u.Id, u.FullName)))
            .ToListAsync(cancellationToken);

        var blocked = revision.Status != RevisionStatus.PendingReview
            ? $"Revision is not pending review (status {revision.Status})."
            : author.Status != UserStatus.Active
                ? SuspendedReason
                : null;

        // While a revision is pending, the content of the article is what readers see now.
        var published = new PublishedContent(
            article.Title,
            article.Summary,
            article.Body,
            article.CategoryId is { } publishedCategory ? categories.GetValueOrDefault(publishedCategory) : null,
            article.PublishedVersion,
            article.PublishedAt!.Value);

        return new ReviewRevisionDetails(
            revision.Id,
            revision.ArticleId,
            revision.Number,
            revision.Title,
            revision.Summary,
            revision.Body,
            revision.CategoryId is { } category ? categories.GetValueOrDefault(category) : null,
            revision.Status,
            revision.SubmissionVersion,
            revision.SubmittedAt!.Value,
            published,
            new ReviewArticleAuthor(author.Id, author.FullName, author.Email, author.Status),
            history,
            blocked is null,
            blocked,
            revision.Version);
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

    // A class with initialisers (not a record built by its constructor): the queries above union and order by its members.
    private sealed class QueueRow
    {
        public Guid ArticleId { get; init; }
        public Guid? RevisionId { get; init; }
        public ReviewItemKind Kind { get; init; }
        public string Title { get; init; } = null!;
        public Guid CategoryId { get; init; }
        public string CategoryName { get; init; } = null!;
        public Guid AuthorId { get; init; }
        public string AuthorName { get; init; } = null!;
        public UserStatus AuthorStatus { get; init; }
        public DateTimeOffset SubmittedAt { get; init; }
        public int SubmissionVersion { get; init; }
    }
}
