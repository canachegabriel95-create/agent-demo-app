using ContentReview.Domain.Articles;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

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
            Title = Guard.Text(Clean(title), nameof(title), TitleMinLength, TitleMaxLength),
            Summary = Guard.Body(Clean(summary)?.Trim(), nameof(summary), SummaryMaxLength),
            Body = Guard.Body(Clean(body), nameof(body), BodyMaxLength),
            Status = ArticleStatus.Draft,
            SubmissionVersion = 0,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }

    /// <summary>
    /// Replaces the draft content. Allowed for the owner while the article is a Draft or Rejected; the draft limits apply
    /// (a title of 1-150 characters, a summary of at most 500, a body of at most 20000). Whether the category exists is
    /// checked by the caller, which can ask the database.
    /// </summary>
    /// <exception cref="InvalidTransitionException">The article is not editable in its current status.</exception>
    public void UpdateContent(string title, string? summary, string? body, Guid? categoryId, DateTimeOffset now)
    {
        ArticleLifecycle.EnsureCanPerform(Status, ArticleAction.Edit, ActorKind.Owner);

        var newTitle = Guard.Text(Clean(title), nameof(title), TitleMinLength, TitleMaxLength);
        var newSummary = Guard.Body(Clean(summary)?.Trim(), nameof(summary), SummaryMaxLength);
        var newBody = Guard.Body(Clean(body), nameof(body), BodyMaxLength);
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("categoryId must not be empty.", nameof(categoryId));
        }

        Title = newTitle;
        Summary = newSummary;
        Body = newBody;
        CategoryId = categoryId;
        UpdatedAt = Guard.Utc(now);
    }

    /// <summary>Whether the owner may delete the article (only a Draft). Throws the 409 conflict when not.</summary>
    /// <exception cref="InvalidTransitionException">The article is not a Draft.</exception>
    public void EnsureCanDelete() => ArticleLifecycle.EnsureCanPerform(Status, ArticleAction.Delete, ActorKind.Owner);

    /// <summary>
    /// Sends the article to review (also a resubmission of a Rejected article) and counts the submission. The submission
    /// rules must be met.
    /// </summary>
    /// <exception cref="InvalidTransitionException">The article cannot be submitted in its current status.</exception>
    /// <exception cref="ValidationException">The article does not meet the submission rules; nothing changes.</exception>
    public void Submit(DateTimeOffset now)
    {
        ArticleLifecycle.EnsureCanPerform(Status, ArticleAction.Submit, ActorKind.Owner);

        var errors = ArticleSubmissionValidator.Validate(this);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var timestamp = Guard.Utc(now);
        Status = ArticleLifecycle.Next(Status, ArticleAction.Submit)!.Value;
        SubmissionVersion++;
        SubmittedAt = timestamp;
        UpdatedAt = timestamp;
    }

    /// <summary>Takes the article back from review; it becomes an editable Draft again.</summary>
    /// <exception cref="InvalidTransitionException">The article is not pending review.</exception>
    public void Withdraw(DateTimeOffset now)
    {
        ArticleLifecycle.EnsureCanPerform(Status, ArticleAction.Withdraw, ActorKind.Owner);

        Status = ArticleLifecycle.Next(Status, ArticleAction.Withdraw)!.Value;
        UpdatedAt = Guard.Utc(now);
    }

    /// <summary>An administrator publishes the article.</summary>
    /// <exception cref="InvalidTransitionException">The article is not pending review.</exception>
    public void Approve(DateTimeOffset now)
    {
        ArticleLifecycle.EnsureCanPerform(Status, ArticleAction.Approve, ActorKind.Administrator);

        var timestamp = Guard.Utc(now);
        Status = ArticleLifecycle.Next(Status, ArticleAction.Approve)!.Value;
        PublishedAt = timestamp;
        UpdatedAt = timestamp;
    }

    /// <summary>An administrator sends the article back to its author. The reason is kept on the review, not here.</summary>
    /// <exception cref="InvalidTransitionException">The article is not pending review.</exception>
    public void Reject(DateTimeOffset now)
    {
        ArticleLifecycle.EnsureCanPerform(Status, ArticleAction.Reject, ActorKind.Administrator);

        Status = ArticleLifecycle.Next(Status, ArticleAction.Reject)!.Value;
        UpdatedAt = Guard.Utc(now);
    }

    /// <summary>The actions the given actor may take on this article now.</summary>
    public IReadOnlyList<ArticleAction> AllowedActions(ActorKind actor) => ArticleLifecycle.AllowedActions(Status, actor);

    /// <summary>PostgreSQL text cannot hold a NUL character, so it is removed instead of failing later with a 500.</summary>
    private static string? Clean(string? text) => text?.Replace("\0", string.Empty, StringComparison.Ordinal);
}
