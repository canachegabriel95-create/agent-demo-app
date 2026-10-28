using ContentReview.Domain.Common;

namespace ContentReview.Domain.Entities;

/// <summary>
/// A version of an article that was published once and has been replaced by an approved revision. The article itself
/// always holds the current published version; this is the history behind it.
/// </summary>
public class ArticlePublishedVersion
{
    private ArticlePublishedVersion()
    {
    }

    public Guid Id { get; private set; }
    public Guid ArticleId { get; private set; }

    /// <summary>1 for the first publication, 2 for the first approved revision, and so on.</summary>
    public int VersionNumber { get; private set; }

    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset SupersededAt { get; private set; }

    /// <summary>The revision whose approval replaced this version.</summary>
    public Guid SupersededByRevisionId { get; private set; }

    public static ArticlePublishedVersion Create(
        Guid articleId,
        int versionNumber,
        string title,
        string summary,
        string body,
        Guid categoryId,
        DateTimeOffset publishedAt,
        DateTimeOffset supersededAt,
        Guid supersededByRevisionId)
    {
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), versionNumber, "Version number starts at 1.");
        }

        return new ArticlePublishedVersion
        {
            Id = Guid.CreateVersion7(),
            ArticleId = Guard.NotEmpty(articleId, nameof(articleId)),
            VersionNumber = versionNumber,
            Title = Guard.Text(title, nameof(title), Article.TitleMinLength, Article.TitleMaxLength),
            Summary = Guard.Text(summary, nameof(summary), 1, Article.SummaryMaxLength),
            Body = Guard.Text(body, nameof(body), 1, Article.BodyMaxLength),
            CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId)),
            PublishedAt = Guard.Utc(publishedAt),
            SupersededAt = Guard.Utc(supersededAt),
            SupersededByRevisionId = Guard.NotEmpty(supersededByRevisionId, nameof(supersededByRevisionId)),
        };
    }
}
