using ContentReview.Domain.Common;

namespace ContentReview.Infrastructure.Articles;

/// <summary>What every signed-in author may read: the published articles of all authors, and nothing else.</summary>
public interface IFeedService
{
    /// <summary>Published articles, newest publication first.</summary>
    Task<PagedResult<FeedItem>> ListAsync(FeedFilter filter, CancellationToken cancellationToken = default);

    /// <summary>One published article. An id that is unknown or not published is the same <c>NotFoundException</c>.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException"/>
    Task<FeedArticle> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
