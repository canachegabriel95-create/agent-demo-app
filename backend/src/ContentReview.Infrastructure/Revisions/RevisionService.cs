using ContentReview.Domain.Articles;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.Infrastructure.Revisions;

public sealed class RevisionService(ContentReviewDbContext db, TimeProvider timeProvider) : IRevisionService
{
    public const string StaleVersionMessage = "The revision was changed by someone else. Reload it and try again.";
    public const string RaceMessage = "The revision has changed. Refresh to see its current status.";
    public const string OpenRevisionMessage = "This article already has a revision in progress.";

    public async Task<RevisionDto> CreateAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var article = await db.Articles.AsNoTracking()
                          .FirstOrDefaultAsync(a => a.Id == articleId && a.AuthorId == authorId, cancellationToken)
                      ?? throw new NotFoundException();

        // Published is the only status that can be revised; the lifecycle says it, with the status in the message.
        var revision = ArticleRevision.CreateFrom(article, timeProvider.GetUtcNow());
        if (await db.ArticleRevisions.AnyAsync(r => r.ArticleId == articleId && r.Status != RevisionStatus.Approved, cancellationToken))
        {
            throw new ConflictException(OpenRevisionMessage);
        }

        db.ArticleRevisions.Add(revision);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests started a revision at the same moment: the unique index let exactly one through.
            throw new ConflictException(OpenRevisionMessage);
        }

        return await ToDtoAsync(revision, cancellationToken);
    }

    public async Task<IReadOnlyList<RevisionDto>> ListAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default)
    {
        if (!await db.Articles.AnyAsync(a => a.Id == articleId && a.AuthorId == authorId, cancellationToken))
        {
            throw new NotFoundException();
        }

        var revisions = await db.ArticleRevisions.AsNoTracking()
            .Where(r => r.ArticleId == articleId)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var result = new List<RevisionDto>(revisions.Count);
        foreach (var revision in revisions)
        {
            result.Add(await ToDtoAsync(revision, cancellationToken));
        }

        return result;
    }

    public async Task<RevisionDto> GetAsync(Guid authorId, Guid revisionId, CancellationToken cancellationToken = default) =>
        await ToDtoAsync(await FindOwnedAsync(authorId, revisionId, tracking: false, cancellationToken), cancellationToken);

    public async Task<RevisionDto> UpdateAsync(
        Guid authorId,
        Guid revisionId,
        string? title,
        string? summary,
        string? body,
        Guid? categoryId,
        uint version,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var revision = await FindOwnedAsync(authorId, revisionId, tracking: true, cancellationToken);

        // What is allowed comes first, then what was sent, then whether the copy the author edited is still current.
        RevisionLifecycle.EnsureCanPerform(revision.Status, ArticleAction.Edit, ActorKind.Owner);
        var content = await DraftContentValidator.ValidateAsync(db, title, summary, body, categoryId, cancellationToken);
        if (revision.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, revision.Version);
        }

        revision.UpdateContent(content.Title, content.Summary, content.Body, content.CategoryId, timeProvider.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await StaleAsync(revisionId, cancellationToken);
        }

        return await ToDtoAsync(revision, cancellationToken);
    }

    public async Task DeleteAsync(Guid authorId, Guid revisionId, uint version, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var revision = await FindOwnedAsync(authorId, revisionId, tracking: true, cancellationToken);

        revision.EnsureCanDelete();
        if (revision.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, revision.Version);
        }

        // A rejected revision has decisions; they go with it (the foreign key cascades).
        db.ArticleRevisions.Remove(revision);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await StaleAsync(revisionId, cancellationToken);
        }
    }

    public async Task<RevisionDto> SubmitAsync(Guid authorId, Guid revisionId, uint version, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var revision = await FindOwnedAsync(authorId, revisionId, tracking: true, cancellationToken);

        RevisionLifecycle.EnsureCanPerform(revision.Status, ArticleAction.Submit, ActorKind.Owner);
        if (revision.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, revision.Version);
        }

        revision.Submit(timeProvider.GetUtcNow());
        await SaveTransitionAsync(cancellationToken);
        return await ToDtoAsync(revision, cancellationToken);
    }

    public async Task<RevisionDto> WithdrawAsync(Guid authorId, Guid revisionId, uint version, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var revision = await FindOwnedAsync(authorId, revisionId, tracking: true, cancellationToken);

        RevisionLifecycle.EnsureCanPerform(revision.Status, ArticleAction.Withdraw, ActorKind.Owner);
        if (revision.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, revision.Version);
        }

        revision.Withdraw(timeProvider.GetUtcNow());
        await SaveTransitionAsync(cancellationToken);
        return await ToDtoAsync(revision, cancellationToken);
    }

    /// <summary>
    /// Saves a status change. The UPDATE only matches the <c>xmin</c> that was read, so when a withdrawal or a decision
    /// got in first this one changes nothing and the author is told to refresh.
    /// </summary>
    private async Task SaveTransitionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(RaceMessage);
        }
    }

    private async Task EnsureActiveAuthorAsync(Guid authorId, CancellationToken cancellationToken)
    {
        var author = await db.Users.AsNoTracking()
                         .FirstOrDefaultAsync(u => u.Id == authorId && u.Role == UserRole.Author, cancellationToken)
                     ?? throw new NotFoundException();
        author.EnsureAuthorActive();
    }

    /// <summary>The revision of one of the author's articles; anybody else's is a 404.</summary>
    private async Task<ArticleRevision> FindOwnedAsync(Guid authorId, Guid revisionId, bool tracking, CancellationToken cancellationToken)
    {
        // The ownership check is a subquery, not a join with AsNoTracking: that would make the whole query untracked.
        var query = tracking ? db.ArticleRevisions.AsQueryable() : db.ArticleRevisions.AsNoTracking();
        return await query
                   .Where(r => r.Id == revisionId && db.Articles.Any(a => a.Id == r.ArticleId && a.AuthorId == authorId))
                   .FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException();
    }

    private async Task<Exception> StaleAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        var current = await db.ArticleRevisions.AsNoTracking()
            .Where(r => r.Id == revisionId)
            .Select(r => (uint?)r.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return current is { } version ? new StaleVersionException(StaleVersionMessage, version) : new NotFoundException();
    }

    private async Task<RevisionDto> ToDtoAsync(ArticleRevision revision, CancellationToken cancellationToken)
    {
        ArticleCategory? category = null;
        if (revision.CategoryId is { } categoryId)
        {
            category = await db.Categories.AsNoTracking()
                .Where(c => c.Id == categoryId)
                .Select(c => new ArticleCategory(c.Id, c.Name))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var reviews = await db.RevisionReviews.AsNoTracking()
            .Where(r => r.RevisionId == revision.Id)
            .OrderByDescending(r => r.SubmissionVersion)
            .ThenByDescending(r => r.DecidedAt)
            .Select(r => new RevisionReviewDto(r.SubmissionVersion, r.Decision, r.Reason, r.DecidedAt))
            .ToListAsync(cancellationToken);

        var latest = reviews.Count == 0 ? null : new LatestReview(reviews[0].Decision, reviews[0].Reason, reviews[0].DecidedAt);

        return new RevisionDto(
            revision.Id,
            revision.ArticleId,
            revision.Title,
            revision.Summary,
            revision.Body,
            category,
            revision.Status,
            revision.SubmissionVersion,
            revision.CreatedAt,
            revision.UpdatedAt,
            revision.SubmittedAt,
            latest,
            reviews,
            revision.AllowedActions(ActorKind.Owner).Select(a => a.ToString().ToLowerInvariant()).ToList(),
            revision.Version);
    }
}
