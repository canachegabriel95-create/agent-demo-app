namespace ContentReview.Infrastructure.Revisions;

/// <summary>
/// An author's revisions of their own published articles. The author id always comes from the session; a revision (or
/// article) of someone else is reported as not found, exactly like one that does not exist.
/// </summary>
public interface IRevisionService
{
    /// <summary>Starts a revision of a Published article as a Draft copy of its content.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's article.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The article is not Published (409).</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">The article already has an open revision (409).</exception>
    Task<RevisionDto> CreateAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default);

    /// <summary>All revisions of the article, newest first (open and approved).</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's article.</exception>
    Task<IReadOnlyList<RevisionDto>> ListAsync(Guid authorId, Guid articleId, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's revision.</exception>
    Task<RevisionDto> GetAsync(Guid authorId, Guid revisionId, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's revision.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The revision is not editable in its status (409).</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">A draft limit is broken or the category does not exist.</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    Task<RevisionDto> UpdateAsync(
        Guid authorId,
        Guid revisionId,
        string? title,
        string? summary,
        string? body,
        Guid? categoryId,
        uint version,
        CancellationToken cancellationToken = default);

    /// <summary>Discards a Draft or Rejected revision; the published article is not affected.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's revision.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The revision is pending review or approved (409).</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    Task DeleteAsync(Guid authorId, Guid revisionId, uint version, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's revision.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The revision cannot be submitted in its status (409).</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">The revision does not meet the submission rules (400).</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">Another request changed the revision at the same time (409).</exception>
    Task<RevisionDto> SubmitAsync(Guid authorId, Guid revisionId, uint version, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's revision.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The revision is not pending review (409).</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">An administrator decided at the same time (409).</exception>
    Task<RevisionDto> WithdrawAsync(Guid authorId, Guid revisionId, uint version, CancellationToken cancellationToken = default);
}
