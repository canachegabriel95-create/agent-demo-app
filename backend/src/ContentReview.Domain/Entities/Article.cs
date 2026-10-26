using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Entities;

public class Article
{
    public const int TitleMinLength = 1;
    public const int TitleMaxLength = 150;
    public const int SummaryMaxLength = 500;
    public const int BodyMaxLength = 20000;

    private Article()
    {
    }

    public Guid Id { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public ArticleStatus Status { get; private set; }

    /// <summary>Number of times the article has been submitted for review (0 while never submitted).</summary>
    public int SubmissionVersion { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    /// <summary>Creates a draft using the draft limits (submit-time rules are enforced by the submit workflow).</summary>
    public static Article CreateDraft(
        Guid authorId,
        string title,
        string? summary,
        string? body,
        Guid? categoryId,
        DateTimeOffset now)
    {
        var timestamp = Guard.Utc(now);

        return new Article
        {
            Id = Guid.CreateVersion7(),
            AuthorId = Guard.NotEmpty(authorId, nameof(authorId)),
            CategoryId = categoryId == Guid.Empty ? throw new ArgumentException("categoryId must not be empty.", nameof(categoryId)) : categoryId,
            Title = Guard.Text(title, nameof(title), TitleMinLength, TitleMaxLength),
            Summary = Guard.Body(summary?.Trim(), nameof(summary), SummaryMaxLength),
            Body = Guard.Body(body, nameof(body), BodyMaxLength),
            Status = ArticleStatus.Draft,
            SubmissionVersion = 0,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }
}
