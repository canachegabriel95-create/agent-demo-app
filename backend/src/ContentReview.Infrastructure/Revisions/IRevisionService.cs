using ContentReview.Infrastructure.Articles;

namespace ContentReview.Infrastructure.Revisions;

/// <summary>
/// An author's revisions of their own published articles. An article has at most one open revision, so the article id
/// identifies it. The author id always comes from the session; somebody else's article is reported as not found.
/// </summary>
public interface IRevisionService
{
    /// <summary>Starts a revision as a copy of the published article. Suspended authors are refused.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's article.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The article is not published (409).</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">The article already has an open revision (409).</exception>
    Task<RevisionDto> CreateAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, somebody else's article, or no open revision.</exception>
    Task<RevisionDto> GetAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the content of a Draft or Rejected revision (the draft limits apply).</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, somebody else's article, or no open revision.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The revision is not editable in its status (409).</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">A draft limit is broken or the category does not exist.</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    Task<RevisionDto> UpdateAsync(
        Guid authorId,
        Guid articleId,
        string? title,
        string? summary,
        string? body,
        Guid? categoryId,
        uint version,
        CancellationToken cancellationToken = default);

    /// <summary>Sends the revision, or resubmits a rejected one, for review under the full submission rules.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, somebody else's article, or no open revision.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The revision cannot be submitted in its status (409).</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The content does not meet the submission rules (400).</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">Another request changed the revision at the same time (409).</exception>
    Task<RevisionDto> SubmitAsync(Guid authorId, Guid articleId, uint version, CancellationToken cancellationToken = default);

    /// <summary>Takes the revision back from review; it becomes an editable Draft.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, somebody else's article, or no open revision.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The revision is not pending review (409).</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">An administrator decided at the same time (409).</exception>
    Task<RevisionDto> WithdrawAsync(Guid authorId, Guid articleId, uint version, CancellationToken cancellationToken = default);

    /// <summary>The decisions on all revisions of the article, newest first, without who made them.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's article.</exception>
    Task<IReadOnlyList<RevisionReviewDto>> ListReviewsAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default);
}
