using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.Infrastructure.Reviews;

public sealed class RevisionReviewService(ContentReviewDbContext db, TimeProvider timeProvider) : IRevisionReviewService
{
    public const string SuspendedMessage = ReviewDecisionService.SuspendedMessage;
    public const string RaceMessage = "This revision has changed since you opened it. Refresh to see its current status.";
    public const string NewerSubmissionMessage = "The author submitted a newer version. Refresh to review it.";

    public async Task<ReviewRevisionDetails> GetAsync(Guid revisionId, CancellationToken cancellationToken = default)
    {
        var revision = await db.ArticleRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken)
                       ?? throw new NotFoundException();
        if (revision.SubmissionVersion == 0)
        {
            throw new NotFoundException();
        }

        var article = await db.Articles.AsNoTracking().FirstAsync(a => a.Id == revision.ArticleId, cancellationToken);
        var author = await db.Users.AsNoTracking().FirstAsync(u => u.Id == article.AuthorId, cancellationToken);

        var categoryIds = new[] { revision.CategoryId, article.CategoryId }.Where(id => id is not null).Select(id => id!.Value).ToList();
        var categories = await db.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new ArticleCategory(c.Id, c.Name), cancellationToken);
        ArticleCategory? CategoryOf(Guid? id) => id is { } key ? categories.GetValueOrDefault(key) : null;

        var history = await (
                from r in db.RevisionReviews.AsNoTracking()
                where r.RevisionId == revisionId
                join u in db.Users.AsNoTracking() on r.ReviewerId equals u.Id
                orderby r.SubmissionVersion descending, r.DecidedAt descending
                select new ReviewHistoryEntry(
                    r.SubmissionVersion, r.Decision, r.Reason, r.DecidedAt, new ReviewerRef(u.Id, u.FullName)))
            .ToListAsync(cancellationToken);

        var blocked = revision.Status != RevisionStatus.PendingReview
            ? $"Revision is not pending review (status {revision.Status})."
            : author.Status != UserStatus.Active
                ? ReviewQueueService.SuspendedReason
                : null;

        // The version readers see now. The article holds it; an approved revision replaced the one shown before.
        var published = new PublishedVersionView(
            article.PublishedVersion,
            article.Title,
            article.Summary,
            article.Body,
            CategoryOf(article.CategoryId),
            article.CurrentVersionPublishedAt ?? article.PublishedAt!.Value);

        return new ReviewRevisionDetails(
            revision.Id,
            revision.ArticleId,
            revision.Title,
            revision.Summary,
            revision.Body,
            CategoryOf(revision.CategoryId),
            revision.Status,
            revision.SubmissionVersion,
            revision.SubmittedAt,
            new ReviewArticleAuthor(author.Id, author.FullName, author.Email, author.Status),
            published,
            history,
            blocked is null,
            blocked,
            revision.Version);
    }

    public Task<ReviewRevisionDetails> ApproveAsync(
        Guid revisionId, Guid reviewerId, uint version, int submissionVersion, CancellationToken cancellationToken = default) =>
        DecideAsync(revisionId, reviewerId, version, submissionVersion, ReviewDecision.Approved, null, cancellationToken);

    public Task<ReviewRevisionDetails> RejectAsync(
        Guid revisionId, Guid reviewerId, uint version, int submissionVersion, string? reason, CancellationToken cancellationToken = default) =>
        DecideAsync(revisionId, reviewerId, version, submissionVersion, ReviewDecision.Rejected, reason, cancellationToken);

    private async Task<ReviewRevisionDetails> DecideAsync(
        Guid revisionId,
        Guid reviewerId,
        uint version,
        int submissionVersion,
        ReviewDecision decision,
        string? reason,
        CancellationToken cancellationToken)
    {
        // Order: it exists, the author may be reviewed, the submission is the one that was opened, then the input.
        var revision = await db.ArticleRevisions.FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken)
                       ?? throw new NotFoundException();
        if (revision.SubmissionVersion == 0)
        {
            throw new NotFoundException();
        }

        var article = await db.Articles.FirstAsync(a => a.Id == revision.ArticleId, cancellationToken);
        var authorStatus = await db.Users.AsNoTracking()
            .Where(u => u.Id == article.AuthorId)
            .Select(u => u.Status)
            .FirstAsync(cancellationToken);
        if (authorStatus != UserStatus.Active)
        {
            throw Conflict(SuspendedMessage, revision);
        }

        if (revision.Status != RevisionStatus.PendingReview)
        {
            throw Conflict($"This revision is no longer pending review (status {revision.Status}).", revision);
        }

        if (revision.SubmissionVersion != submissionVersion)
        {
            throw Conflict(NewerSubmissionMessage, revision);
        }

        if (revision.Version != version)
        {
            throw Conflict(RaceMessage, revision);
        }

        var cleanReason = decision == ReviewDecision.Rejected ? ValidReason(reason) : null;

        var now = timeProvider.GetUtcNow();
        if (decision == ReviewDecision.Approved)
        {
            // The revision becomes final and its content becomes the published content, in this one SaveChanges: the
            // replaced version is kept, the article's xmin guards the swap, the revision's xmin guards the decision.
            revision.Approve(now);
            db.ArticlePublishedVersions.Add(article.ReplaceContentWith(revision, now));
        }
        else
        {
            revision.Reject(now);
        }

        db.RevisionReviews.Add(RevisionReview.Create(revision.Id, revision.SubmissionVersion, reviewerId, decision, cleanReason, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await CurrentConflictAsync(revisionId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw await CurrentConflictAsync(revisionId, cancellationToken);
        }

        return await GetAsync(revisionId, cancellationToken);
    }

    private static string ValidReason(string? reason)
    {
        var text = (reason ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (text.Length == 0)
        {
            throw new ValidationException("reason", "Reason is required.");
        }

        if (text.Length < Review.ReasonMinLength || text.Length > Review.ReasonMaxLength)
        {
            throw new ValidationException(
                "reason", $"Reason must be between {Review.ReasonMinLength} and {Review.ReasonMaxLength} characters.");
        }

        return text;
    }

    private static ReviewConflictException Conflict(string message, ArticleRevision revision) =>
        new(message, revision.Status, revision.Version);

    /// <summary>Somebody got in first: say what the revision is now, read afresh.</summary>
    private async Task<Exception> CurrentConflictAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        var current = await db.ArticleRevisions.AsNoTracking()
            .Where(r => r.Id == revisionId)
            .Select(r => new { r.Status, r.Version })
            .FirstOrDefaultAsync(cancellationToken);

        return current is null
            ? new NotFoundException()
            : new ReviewConflictException(RaceMessage, current.Status, current.Version);
    }
}
