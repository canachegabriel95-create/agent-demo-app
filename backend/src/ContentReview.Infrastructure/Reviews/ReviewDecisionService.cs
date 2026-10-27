using ContentReview.Domain.Auditing;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.Infrastructure.Reviews;

public sealed class ReviewDecisionService(
    ContentReviewDbContext db,
    IReviewQueueService queue,
    IAuditWriter audit,
    TimeProvider timeProvider) : IReviewDecisionService
{
    public const string SuspendedMessage = "Review is blocked because the author account is suspended.";
    public const string RaceMessage = "This article has changed since you opened it. Refresh to see its current status.";
    public const string NewerSubmissionMessage = "The author submitted a newer version. Refresh to review it.";

    public Task<ReviewArticleDetails> ApproveAsync(
        Guid articleId, Guid reviewerId, uint version, int submissionVersion, CancellationToken cancellationToken = default) =>
        DecideAsync(articleId, reviewerId, version, submissionVersion, ReviewDecision.Approved, null, cancellationToken);

    public Task<ReviewArticleDetails> RejectAsync(
        Guid articleId, Guid reviewerId, uint version, int submissionVersion, string? reason, CancellationToken cancellationToken = default) =>
        DecideAsync(articleId, reviewerId, version, submissionVersion, ReviewDecision.Rejected, reason, cancellationToken);

    private async Task<ReviewArticleDetails> DecideAsync(
        Guid articleId,
        Guid reviewerId,
        uint version,
        int submissionVersion,
        ReviewDecision decision,
        string? reason,
        CancellationToken cancellationToken)
    {
        // Order: it exists, the author may be reviewed, the submission is the one that was opened, then the input.
        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == articleId, cancellationToken)
                      ?? throw new NotFoundException();
        if (article.SubmissionVersion == 0)
        {
            throw new NotFoundException();
        }

        var authorStatus = await db.Users.AsNoTracking()
            .Where(u => u.Id == article.AuthorId)
            .Select(u => u.Status)
            .FirstAsync(cancellationToken);
        if (authorStatus != UserStatus.Active)
        {
            throw Conflict(SuspendedMessage, article);
        }

        if (article.Status != ArticleStatus.PendingReview)
        {
            throw Conflict($"This article is no longer pending review (status {article.Status}).", article);
        }

        if (article.SubmissionVersion != submissionVersion)
        {
            throw Conflict(NewerSubmissionMessage, article);
        }

        if (article.Version != version)
        {
            throw Conflict(RaceMessage, article);
        }

        var cleanReason = decision == ReviewDecision.Rejected ? ValidReason(reason) : null;

        var now = timeProvider.GetUtcNow();
        if (decision == ReviewDecision.Approved)
        {
            article.Approve(now);
        }
        else
        {
            article.Reject(now);
        }

        db.Reviews.Add(Review.Create(article.Id, article.SubmissionVersion, reviewerId, decision, cleanReason, now));
        audit.Record(
            decision == ReviewDecision.Approved ? AuditActions.ArticleApproved : AuditActions.ArticleRejected,
            nameof(Article),
            article.Id,
            reviewerId,
            decision == ReviewDecision.Approved
                ? new { articleId = article.Id, submissionVersion }
                : new { articleId = article.Id, submissionVersion, reason = cleanReason });

        try
        {
            // The UPDATE only matches the xmin that was read and the (article, submission) review is unique: of any
            // number of racing decisions or withdrawals exactly one gets through.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await CurrentConflictAsync(articleId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw await CurrentConflictAsync(articleId, cancellationToken);
        }

        return await queue.GetAsync(articleId, cancellationToken);
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

    private static ReviewConflictException Conflict(string message, Article article) =>
        new(message, article.Status, article.Version);

    /// <summary>Somebody got in first: say what the article is now, read afresh.</summary>
    private async Task<Exception> CurrentConflictAsync(Guid articleId, CancellationToken cancellationToken)
    {
        var current = await db.Articles.AsNoTracking()
            .Where(a => a.Id == articleId)
            .Select(a => new { a.Status, a.Version })
            .FirstOrDefaultAsync(cancellationToken);

        return current is null
            ? new NotFoundException()
            : new ReviewConflictException(RaceMessage, current.Status, current.Version);
    }
}
