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
    public const string AlreadyOpenMessage = "This article already has a revision in progress.";

    public async Task<RevisionDto> CreateAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var article = await FindOwnedArticleAsync(authorId, articleId, cancellationToken);

        article.EnsureCanRevise();
        if (await db.ArticleRevisions.AnyAsync(r => r.ArticleId == articleId && r.Status != RevisionStatus.Approved, cancellationToken))
        {
            throw new ConflictException(AlreadyOpenMessage);
        }

        var number = await db.ArticleRevisions.CountAsync(r => r.ArticleId == articleId, cancellationToken) + 1;
        var revision = ArticleRevision.CreateFrom(article, number, timeProvider.GetUtcNow());
        db.ArticleRevisions.Add(revision);

        await SaveTransitionAsync(cancellationToken, AlreadyOpenMessage);
        return await ToDtoAsync(revision, cancellationToken);
    }

    public async Task<RevisionDto> GetAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default)
    {
        await FindOwnedArticleAsync(authorId, articleId, cancellationToken);
        return await ToDtoAsync(await FindOpenAsync(articleId, cancellationToken), cancellationToken);
    }

    public async Task<RevisionDto> UpdateAsync(
        Guid authorId,
        Guid articleId,
        string? title,
        string? summary,
        string? body,
        Guid? categoryId,
        uint version,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        await FindOwnedArticleAsync(authorId, articleId, cancellationToken);
        var revision = await FindOpenAsync(articleId, cancellationToken);

        // What is allowed comes first, then what was sent, then whether the copy the author edited is still current.
        RevisionLifecycle.EnsureCanPerform(revision.Status, RevisionAction.Edit, ActorKind.Owner);
        var content = await DraftContent.ValidateAsync(db, title, summary, body, categoryId, cancellationToken);
        if (revision.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, revision.Version);
        }

        revision.UpdateContent(content.Title, content.Summary, content.Body, content.CategoryId, timeProvider.GetUtcNow());
        try
        {
            // The UPDATE is conditional on the xmin that was read, so a racing change surfaces here.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await StaleAsync(revision.Id, cancellationToken);
        }

        return await ToDtoAsync(revision, cancellationToken);
    }

    public async Task<RevisionDto> SubmitAsync(Guid authorId, Guid articleId, uint version, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        await FindOwnedArticleAsync(authorId, articleId, cancellationToken);
        var revision = await FindOpenAsync(articleId, cancellationToken);

        RevisionLifecycle.EnsureCanPerform(revision.Status, RevisionAction.Submit, ActorKind.Owner);
        if (revision.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, revision.Version);
        }

        revision.Submit(timeProvider.GetUtcNow());

        await SaveTransitionAsync(cancellationToken, RaceMessage);
        return await ToDtoAsync(revision, cancellationToken);
    }

    public async Task<RevisionDto> WithdrawAsync(Guid authorId, Guid articleId, uint version, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        await FindOwnedArticleAsync(authorId, articleId, cancellationToken);
        var revision = await FindOpenAsync(articleId, cancellationToken);

        RevisionLifecycle.EnsureCanPerform(revision.Status, RevisionAction.Withdraw, ActorKind.Owner);
        if (revision.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, revision.Version);
        }

        revision.Withdraw(timeProvider.GetUtcNow());

        await SaveTransitionAsync(cancellationToken, RaceMessage);
        return await ToDtoAsync(revision, cancellationToken);
    }

    public async Task<IReadOnlyList<RevisionReviewDto>> ListReviewsAsync(
        Guid authorId, Guid articleId, CancellationToken cancellationToken = default)
    {
        await FindOwnedArticleAsync(authorId, articleId, cancellationToken);

        return await (
                from review in db.RevisionReviews.AsNoTracking()
                join revision in db.ArticleRevisions.AsNoTracking() on review.RevisionId equals revision.Id
                where revision.ArticleId == articleId
                orderby revision.Number descending, review.SubmissionVersion descending, review.DecidedAt descending
                select new RevisionReviewDto(revision.Number, review.SubmissionVersion, review.Decision, review.Reason, review.DecidedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Saves a status change. The UPDATE only matches the <c>xmin</c> that was read, so when a withdraw, a decision or a
    /// second submit got in first this one changes nothing and the author is told to refresh.
    /// </summary>
    private async Task SaveTransitionAsync(CancellationToken cancellationToken, string conflictMessage)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(RaceMessage);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two creates of the same revision raced: the unique "one open revision per article" index let one through.
            throw new ConflictException(conflictMessage);
        }
    }

    private async Task EnsureActiveAuthorAsync(Guid authorId, CancellationToken cancellationToken)
    {
        var author = await db.Users.AsNoTracking()
                         .FirstOrDefaultAsync(u => u.Id == authorId && u.Role == UserRole.Author, cancellationToken)
                     ?? throw new NotFoundException();
        author.EnsureAuthorActive();
    }

    private async Task<Article> FindOwnedArticleAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken) =>
        await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == articleId && a.AuthorId == authorId, cancellationToken)
        ?? throw new NotFoundException();

    private async Task<ArticleRevision> FindOpenAsync(Guid articleId, CancellationToken cancellationToken) =>
        await db.ArticleRevisions.FirstOrDefaultAsync(
            r => r.ArticleId == articleId && r.Status != RevisionStatus.Approved, cancellationToken)
        ?? throw new NotFoundException();

    /// <summary>The revision was changed or removed between reading it and saving: say what is current now.</summary>
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

        var latestReview = await db.RevisionReviews.AsNoTracking()
            .Where(r => r.RevisionId == revision.Id)
            .OrderByDescending(r => r.SubmissionVersion)
            .ThenByDescending(r => r.DecidedAt)
            .Select(r => new LatestRevisionReview(r.Decision, r.Reason, r.DecidedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return new RevisionDto(
            revision.Id,
            revision.ArticleId,
            revision.Number,
            revision.Title,
            revision.Summary,
            revision.Body,
            category,
            revision.Status,
            revision.SubmissionVersion,
            revision.CreatedAt,
            revision.UpdatedAt,
            revision.SubmittedAt,
            latestReview,
            revision.AllowedActions(ActorKind.Owner).Select(a => a.ToString().ToLowerInvariant()).ToList(),
            revision.Version);
    }
}
