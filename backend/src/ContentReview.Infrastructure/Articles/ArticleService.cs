using ContentReview.Domain.Articles;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Articles;

public sealed class ArticleService(ContentReviewDbContext db, TimeProvider timeProvider) : IArticleService
{
    public const string StaleVersionMessage = "The article was changed by someone else. Reload it and try again.";

    public async Task<ArticleDto> CreateAsync(
        Guid authorId, string? title, string? summary, string? body, Guid? categoryId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var content = await ValidatedAsync(title, summary, body, categoryId, cancellationToken);

        var article = Article.CreateDraft(authorId, content.Title, content.Summary, content.Body, content.CategoryId, timeProvider.GetUtcNow());
        db.Articles.Add(article);
        await db.SaveChangesAsync(cancellationToken);

        return await ToDtoAsync(article, cancellationToken);
    }

    public async Task<ArticleDto> GetAsync(Guid authorId, Guid id, CancellationToken cancellationToken = default) =>
        await ToDtoAsync(await FindOwnedAsync(authorId, id, cancellationToken), cancellationToken);

    public async Task<ArticleDto> UpdateAsync(
        Guid authorId,
        Guid id,
        string? title,
        string? summary,
        string? body,
        Guid? categoryId,
        uint version,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var article = await FindOwnedAsync(authorId, id, cancellationToken);

        // What is allowed comes first, then what was sent, then whether the copy the author edited is still current.
        ArticleLifecycle.EnsureCanPerform(article.Status, ArticleAction.Edit, ActorKind.Owner);
        var content = await ValidatedAsync(title, summary, body, categoryId, cancellationToken);
        if (article.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, article.Version);
        }

        article.UpdateContent(content.Title, content.Summary, content.Body, content.CategoryId, timeProvider.GetUtcNow());
        try
        {
            // The UPDATE is conditional on the xmin that was read, so a racing change surfaces here.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await StaleAsync(id, cancellationToken);
        }

        return await ToDtoAsync(article, cancellationToken);
    }

    public async Task DeleteAsync(Guid authorId, Guid id, uint version, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAuthorAsync(authorId, cancellationToken);
        var article = await FindOwnedAsync(authorId, id, cancellationToken);

        article.EnsureCanDelete();
        if (article.Version != version)
        {
            throw new StaleVersionException(StaleVersionMessage, article.Version);
        }

        // A Draft can have a history: rejected, resubmitted, then withdrawn. Its review records block the delete (the
        // foreign key restricts), so they go in the same transaction; the submission snapshots cascade.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Reviews.Where(r => r.ArticleId == id).ExecuteDeleteAsync(cancellationToken);
            db.Articles.Remove(article);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw await StaleAsync(id, cancellationToken);
        }
    }

    private async Task EnsureActiveAuthorAsync(Guid authorId, CancellationToken cancellationToken)
    {
        var author = await db.Users.AsNoTracking()
                         .FirstOrDefaultAsync(u => u.Id == authorId && u.Role == UserRole.Author, cancellationToken)
                     ?? throw new NotFoundException();
        author.EnsureAuthorActive();
    }

    private async Task<Article> FindOwnedAsync(Guid authorId, Guid id, CancellationToken cancellationToken) =>
        await db.Articles.FirstOrDefaultAsync(a => a.Id == id && a.AuthorId == authorId, cancellationToken)
        ?? throw new NotFoundException();

    /// <summary>The article was changed or removed between reading it and saving: say what is current now.</summary>
    private async Task<Exception> StaleAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await db.Articles.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => (uint?)a.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return current is { } version ? new StaleVersionException(StaleVersionMessage, version) : new NotFoundException();
    }

    private sealed record DraftContent(string Title, string Summary, string Body, Guid? CategoryId);

    /// <summary>Applies the draft limits and reports every broken one at once, keyed by field name.</summary>
    private async Task<DraftContent> ValidatedAsync(
        string? title, string? summary, string? body, Guid? categoryId, CancellationToken cancellationToken)
    {
        // NUL cannot be stored, so it is removed before anything is measured.
        var cleanTitle = (title ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        var cleanSummary = (summary ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        var cleanBody = (body ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);

        var errors = new Dictionary<string, string[]>();
        if (cleanTitle.Length == 0)
        {
            errors["title"] = ["Title is required."];
        }
        else if (cleanTitle.Length > Article.TitleMaxLength)
        {
            errors["title"] = [$"Title must be at most {Article.TitleMaxLength} characters."];
        }

        if (cleanSummary.Length > Article.SummaryMaxLength)
        {
            errors["summary"] = [$"Summary must be at most {Article.SummaryMaxLength} characters."];
        }

        if (cleanBody.Length > Article.BodyMaxLength)
        {
            errors["body"] = [$"Body must be at most {Article.BodyMaxLength} characters."];
        }

        if (categoryId is { } category
            && (category == Guid.Empty || !await db.Categories.AnyAsync(c => c.Id == category, cancellationToken)))
        {
            errors["categoryId"] = ["Category does not exist."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new DraftContent(cleanTitle, cleanSummary, cleanBody, categoryId);
    }

    private async Task<ArticleDto> ToDtoAsync(Article article, CancellationToken cancellationToken)
    {
        ArticleCategory? category = null;
        if (article.CategoryId is { } categoryId)
        {
            category = await db.Categories.AsNoTracking()
                .Where(c => c.Id == categoryId)
                .Select(c => new ArticleCategory(c.Id, c.Name))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var latestReview = await db.Reviews.AsNoTracking()
            .Where(r => r.ArticleId == article.Id)
            .OrderByDescending(r => r.SubmissionVersion)
            .ThenByDescending(r => r.DecidedAt)
            .Select(r => new LatestReview(r.Decision, r.Reason, r.DecidedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return new ArticleDto(
            article.Id,
            article.Title,
            article.Summary,
            article.Body,
            category,
            article.Status,
            article.SubmissionVersion,
            article.CreatedAt,
            article.UpdatedAt,
            article.SubmittedAt,
            article.PublishedAt,
            latestReview,
            article.AllowedActions(ActorKind.Owner).Select(a => a.ToString().ToLowerInvariant()).ToList(),
            article.Version);
    }
}
