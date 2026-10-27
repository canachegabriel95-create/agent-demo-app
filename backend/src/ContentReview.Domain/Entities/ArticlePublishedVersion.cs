using ContentReview.Domain.Common;

namespace ContentReview.Domain.Entities;

/// <summary>
/// Immutable snapshot of a version of an article that was published and then replaced by an approved revision. The
/// version that is live now is the article itself, so history is only written when a revision is approved.
/// </summary>
public class ArticlePublishedVersion
{
    private ArticlePublishedVersion()
    {
    }

    public Guid Id { get; private set; }
    public Guid ArticleId { get; private set; }

    /// <summary>1-based number of the published version (the article's <see cref="Article.PublishedVersion"/> at the time).</summary>
    public int Version { get; private set; }

    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public Guid? CategoryId { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset SupersededAt { get; private set; }

    public static ArticlePublishedVersion Capture(Article published, DateTimeOffset supersededAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ArticleId = published.Id,
            Version = published.PublishedVersion,
            Title = published.Title,
            Summary = published.Summary,
            Body = published.Body,
            CategoryId = published.CategoryId,
            PublishedAt = published.PublishedAt ?? throw new InvalidOperationException("The article was never published."),
            SupersededAt = Guard.Utc(supersededAt),
        };
}
