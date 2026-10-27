using ContentReview.Domain.Auditing;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.Infrastructure.Reviews;

public sealed class RevisionDecisionService(
    ContentReviewDbContext db,
    IReviewQueueService queue,
    IAuditWriter audit,
    TimeProvider timeProvider) : IRevisionDecisionService
{
    public const string SuspendedMessage = ReviewDecisionService.SuspendedMessage;
    public const string RaceMessage = "This revision has changed since you opened it. Refresh to see its current status.";
    public const string NewerSubmissionMessage = "The author submitted a newer version of this revision. Refresh to review it.";

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
            // The revision becomes history and its content becomes the published content, in one SaveChanges.
            revision.Approve(now);
            db.ArticlePublishedVersions.Add(article.ApplyRevision(revision, now));
        }
        else
        {
            revision.Reject(now);
        }

        db.RevisionReviews.Add(RevisionReview.Create(revision.Id, revision.SubmissionVersion, reviewerId, decision, cleanReason, now));
        audit.Record(
            decision == ReviewDecision.Approved ? AuditActions.RevisionApproved : AuditActions.RevisionRejected,
            nameof(ArticleRevision),
            revision.Id,
            reviewerId,
            decision == ReviewDecision.Approved
                ? new { articleId = revision.ArticleId, revisionId = revision.Id, revisionNumber = revision.Number, submissionVersion }
                : new { articleId = revision.ArticleId, revisionId = revision.Id, revisionNumber = revision.Number, submissionVersion, reason = cleanReason });

        try
        {
            // The UPDATE only matches the xmin that was read and the (revision, submission) review is unique: of any
            // number of racing decisions or withdrawals exactly one gets through.
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

        return await queue.GetRevisionAsync(revisionId, cancellationToken);
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

    private static RevisionConflictException Conflict(string message, ArticleRevision revision) =>
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
            : new RevisionConflictException(RaceMessage, current.Status, current.Version);
    }
}
