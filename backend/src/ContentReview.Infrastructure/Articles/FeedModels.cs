namespace ContentReview.Infrastructure.Articles;

public sealed record FeedFilter(string? Search, Guid? CategoryId, int Page, int PageSize);

/// <summary>A published article in the feed. The author is only named, never identified: no id, no email.</summary>
public sealed record FeedItem(
    Guid Id,
    string Title,
    string Summary,
    ArticleCategory? Category,
    string AuthorDisplayName,
    DateTimeOffset PublishedAt);

/// <summary>A published article in full, as it was when the administrator approved it.</summary>
public sealed record FeedArticle(
    Guid Id,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    string AuthorDisplayName,
    DateTimeOffset PublishedAt);
