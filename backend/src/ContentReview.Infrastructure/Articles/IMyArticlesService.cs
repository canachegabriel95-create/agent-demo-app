using ContentReview.Domain.Common;

namespace ContentReview.Infrastructure.Articles;

/// <summary>An author's overview of their own articles. The author id always comes from the session.</summary>
public interface IMyArticlesService
{
    Task<PagedResult<MyArticleItem>> ListAsync(Guid authorId, MyArticlesFilter filter, CancellationToken cancellationToken = default);

    Task<ArticleStatusCounts> StatsAsync(Guid authorId, CancellationToken cancellationToken = default);
}
