namespace ContentReview.Infrastructure.Reviews;

/// <summary>
/// What administrators do with revisions of published articles: read one next to the version it would replace, and
/// decide on it. Exactly one decision can be recorded per submission of a revision, however many administrators (or the
/// author, withdrawing) act at the same moment.
/// </summary>
public interface IRevisionReviewService
{
    /// <summary>A revision that was submitted at least once, with the published version it would replace.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or a revision that was never submitted (a private draft).</exception>
    Task<ReviewRevisionDetails> GetAsync(Guid revisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves the revision: its content replaces the published content and the replaced version is kept in the
    /// history, all in one transaction.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or never submitted.</exception>
    /// <exception cref="Domain.Exceptions.ReviewConflictException">
    /// The author is suspended, the revision is no longer pending, a newer submission replaced the one reviewed, the
    /// version is stale, or another request decided first (409 with the current status).
    /// </exception>
    Task<ReviewRevisionDetails> ApproveAsync(
        Guid revisionId, Guid reviewerId, uint version, int submissionVersion, CancellationToken cancellationToken = default);

    /// <summary>Sends the revision back to its author with a reason (10-1000 characters); the published version is untouched.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or never submitted.</exception>
    /// <exception cref="Domain.Exceptions.ReviewConflictException">As for <see cref="ApproveAsync"/>.</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The reason is missing or has the wrong length (400).</exception>
    Task<ReviewRevisionDetails> RejectAsync(
        Guid revisionId, Guid reviewerId, uint version, int submissionVersion, string? reason, CancellationToken cancellationToken = default);
}
