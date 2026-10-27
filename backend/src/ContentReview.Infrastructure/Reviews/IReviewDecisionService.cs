namespace ContentReview.Infrastructure.Reviews;

/// <summary>
/// Records an administrator's decision on a submission. Exactly one decision can be recorded per submission, however
/// many administrators (or the author, withdrawing) act at the same moment.
/// </summary>
public interface IReviewDecisionService
{
    /// <summary>Publishes the article. Returns the article as the administrator sees it after the decision.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or never submitted.</exception>
    /// <exception cref="Domain.Exceptions.ReviewConflictException">
    /// The author is suspended, the article is no longer pending, a newer submission replaced the one reviewed, the
    /// version is stale, or another request decided first (409 with the current status).
    /// </exception>
    Task<ReviewArticleDetails> ApproveAsync(
        Guid articleId, Guid reviewerId, uint version, int submissionVersion, CancellationToken cancellationToken = default);

    /// <summary>Sends the article back to its author with a reason (10-1000 characters).</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or never submitted.</exception>
    /// <exception cref="Domain.Exceptions.ReviewConflictException">As for <see cref="ApproveAsync"/>.</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The reason is missing or has the wrong length (400).</exception>
    Task<ReviewArticleDetails> RejectAsync(
        Guid articleId, Guid reviewerId, uint version, int submissionVersion, string? reason, CancellationToken cancellationToken = default);
}
