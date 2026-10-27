namespace ContentReview.Infrastructure.Reviews;

/// <summary>
/// Records an administrator's decision on a submitted revision of a published article. Exactly one decision can be
/// recorded per submission, however many administrators (or the author, withdrawing) act at the same moment.
/// </summary>
public interface IRevisionDecisionService
{
    /// <summary>
    /// Approves the revision: its content replaces the published content in the same transaction and the replaced
    /// version is kept in the history. Returns the revision as the administrator sees it after the decision.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or never submitted.</exception>
    /// <exception cref="Domain.Exceptions.RevisionConflictException">
    /// The author is suspended, the revision is no longer pending, a newer submission replaced the one reviewed, the
    /// version is stale, or another request decided first (409 with the current status).
    /// </exception>
    Task<ReviewRevisionDetails> ApproveAsync(
        Guid revisionId, Guid reviewerId, uint version, int submissionVersion, CancellationToken cancellationToken = default);

    /// <summary>Sends the revision back to its author with a reason (10-1000 characters); the published article is untouched.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or never submitted.</exception>
    /// <exception cref="Domain.Exceptions.RevisionConflictException">As for <see cref="ApproveAsync"/>.</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The reason is missing or has the wrong length (400).</exception>
    Task<ReviewRevisionDetails> RejectAsync(
        Guid revisionId, Guid reviewerId, uint version, int submissionVersion, string? reason, CancellationToken cancellationToken = default);
}
