using ContentReview.Domain.Common;

namespace ContentReview.Domain.Entities;

/// <summary>Immutable snapshot of an article at the moment it was submitted for review.</summary>
public class ArticleSubmission
{
    private ArticleSubmission()
    {
    }

    public Guid Id { get; private set; }
    public Guid ArticleId { get; private set; }

    /// <summary>1-based submission number; unique per article.</summary>
    public int Version { get; private set; }

    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }

    /// <summary>When the author took this submission back from review; <c>null</c> while it was not withdrawn.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static ArticleSubmission Create(
        Guid articleId,
        int version,
        string title,
        string summary,
        string body,
        Guid categoryId,
        DateTimeOffset submittedAt)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Submission version starts at 1.");
        }

        return new ArticleSubmission
        {
            Id = Guid.CreateVersion7(),
            ArticleId = Guard.NotEmpty(articleId, nameof(articleId)),
            Version = version,
            Title = Guard.Text(title, nameof(title), Article.TitleMinLength, Article.TitleMaxLength),
            Summary = Guard.Text(summary, nameof(summary), 1, Article.SummaryMaxLength),
            Body = Guard.Text(body, nameof(body), 1, Article.BodyMaxLength),
            CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId)),
            SubmittedAt = Guard.Utc(submittedAt),
        };
    }

    /// <summary>Marks the submission as taken back by its author. The snapshot itself never changes.</summary>
    public void MarkWithdrawn(DateTimeOffset now) => WithdrawnAt = Guard.Utc(now);
}
