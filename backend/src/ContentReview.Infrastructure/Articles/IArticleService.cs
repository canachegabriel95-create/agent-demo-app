namespace ContentReview.Infrastructure.Articles;

/// <summary>
/// An author's own articles. The author id always comes from the session; an article that belongs to someone else is
/// reported as not found, exactly like one that does not exist.
/// </summary>
public interface IArticleService
{
    /// <summary>Creates a Draft. Only the title is required. Suspended authors are refused.</summary>
    /// <exception cref="Domain.Exceptions.ValidationException">A draft limit is broken or the category does not exist.</exception>
    Task<ArticleDto> CreateAsync(
        Guid authorId, string? title, string? summary, string? body, Guid? categoryId, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's article.</exception>
    Task<ArticleDto> GetAsync(Guid authorId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces the content of a Draft or a Rejected article.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's article.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The article is not editable in its status (409).</exception>
    /// <exception cref="Domain.Exceptions.ValidationException">A draft limit is broken or the category does not exist.</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    Task<ArticleDto> UpdateAsync(
        Guid authorId,
        Guid id,
        string? title,
        string? summary,
        string? body,
        Guid? categoryId,
        uint version,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a Draft, with the decisions recorded for its earlier submissions.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or somebody else's article.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransitionException">The article is not a Draft (409).</exception>
    /// <exception cref="Domain.Exceptions.StaleVersionException"><paramref name="version"/> is no longer current (409).</exception>
    Task DeleteAsync(Guid authorId, Guid id, uint version, CancellationToken cancellationToken = default);
}
