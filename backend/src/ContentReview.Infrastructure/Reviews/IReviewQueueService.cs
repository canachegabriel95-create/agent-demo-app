using ContentReview.Domain.Common;

namespace ContentReview.Infrastructure.Reviews;

/// <summary>What administrators see when they review: the queue of pending submissions and one article in detail.</summary>
public interface IReviewQueueService
{
    /// <summary>Pending articles only, oldest submission first unless asked otherwise. Suspended authors' articles are listed too.</summary>
    Task<PagedResult<ReviewQueueItem>> ListQueueAsync(ReviewQueueFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// The submitted content of an article that has been submitted at least once, in any status. An article that was
    /// never submitted is a private draft and is reported as not found.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Unknown id, or never submitted.</exception>
    Task<ReviewArticleDetails> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Up to 20 authors whose name or email contains <paramref name="search"/>, by name, for the queue's author filter.</summary>
    Task<IReadOnlyList<AuthorLookupItem>> LookupAuthorsAsync(string? search, CancellationToken cancellationToken = default);
}
