using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Users;

namespace ContentReview.Infrastructure.Articles;

public enum MyArticleSort
{
    UpdatedAt,
    SubmittedAt,
    Title,
}

public sealed record MyArticlesFilter(
    string? Search,
    ArticleStatus? Status,
    Guid? CategoryId,
    MyArticleSort Sort,
    SortDirection Direction,
    int Page,
    int PageSize);

/// <summary>One of the author's own articles in their list.</summary>
/// <param name="LatestRejectionReason">The reason of the latest rejection, only while the article is Rejected; otherwise <c>null</c>.</param>
/// <param name="AllowedActions">What the author may do now: <c>edit</c>, <c>delete</c>, <c>submit</c>, <c>withdraw</c>, <c>revise</c>.</param>
/// <param name="OpenRevision">The revision of a published article that is in progress, or <c>null</c>.</param>
public sealed record MyArticleItem(
    Guid Id,
    string Title,
    ArticleCategory? Category,
    ArticleStatus Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? PublishedAt,
    string? LatestRejectionReason,
    IReadOnlyList<string> AllowedActions,
    uint Version,
    OpenRevisionSummary? OpenRevision);

/// <summary>How many of the author's articles are in each status; a status without articles is 0.</summary>
public sealed record ArticleStatusCounts(int Draft, int PendingReview, int Published, int Rejected);
