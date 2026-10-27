using ContentReview.Domain.Articles;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.Domain.Entities;

/// <summary>
/// An edit of a published article, kept apart from it. The published article is not touched until an administrator
/// approves the revision; a rejected revision never reaches readers. There is at most one open revision per article.
/// </summary>
public class ArticleRevision
{
    private ArticleRevision()
    {
    }

    public Guid Id { get; private set; }
    public Guid ArticleId { get; private set; }

    /// <summary>1-based, counts the revisions of the article.</summary>
    public int Number { get; private set; }

    public Guid? CategoryId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public RevisionStatus Status { get; private set; }

    /// <summary>Number of times this revision has been submitted for review (0 while never submitted).</summary>
    public int SubmissionVersion { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>When the author took the latest submission back; cleared by the next submission.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>When an administrator approved the revision and it replaced the published content.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    /// <summary>Starts a revision as a copy of the published article, so the author edits what readers see.</summary>
    /// <exception cref="InvalidTransitionException">The article is not published (409).</exception>
    public static ArticleRevision CreateFrom(Article published, int number, DateTimeOffset now)
    {
        published.EnsureCanRevise();
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Revision number starts at 1.");
        }

        var timestamp = Guard.Utc(now);
        return new ArticleRevision
        {
            Id = Guid.CreateVersion7(),
            ArticleId = published.Id,
            Number = number,
            CategoryId = published.CategoryId,
            Title = published.Title,
            Summary = published.Summary,
            Body = published.Body,
            Status = RevisionStatus.Draft,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }

    /// <summary>Replaces the content while the revision is a Draft or Rejected; the article's draft limits apply.</summary>
    /// <exception cref="InvalidTransitionException">The revision is not editable in its status (409).</exception>
    public void UpdateContent(string title, string? summary, string? body, Guid? categoryId, DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, RevisionAction.Edit, ActorKind.Owner);

        var newTitle = Guard.Text(Clean(title), nameof(title), Article.TitleMinLength, Article.TitleMaxLength);
        var newSummary = Guard.Body(Clean(summary)?.Trim(), nameof(summary), Article.SummaryMaxLength);
        var newBody = Guard.Body(Clean(body), nameof(body), Article.BodyMaxLength);
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

    /// <summary>Sends the revision to review (also a resubmission of a Rejected one); the submission rules apply.</summary>
    /// <exception cref="InvalidTransitionException">The revision cannot be submitted in its status (409).</exception>
    /// <exception cref="ValidationException">The content does not meet the submission rules; nothing changes.</exception>
    public void Submit(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, RevisionAction.Submit, ActorKind.Owner);

        var errors = ArticleSubmissionValidator.Validate(Title, Summary, Body, CategoryId);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var timestamp = Guard.Utc(now);
        Status = RevisionLifecycle.Next(Status, RevisionAction.Submit);
        SubmissionVersion++;
        SubmittedAt = timestamp;
        WithdrawnAt = null;
        UpdatedAt = timestamp;
    }

    /// <summary>Takes the revision back from review; it becomes an editable Draft again.</summary>
    /// <exception cref="InvalidTransitionException">The revision is not pending review (409).</exception>
    public void Withdraw(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, RevisionAction.Withdraw, ActorKind.Owner);

        var timestamp = Guard.Utc(now);
        Status = RevisionLifecycle.Next(Status, RevisionAction.Withdraw);
        WithdrawnAt = timestamp;
        UpdatedAt = timestamp;
    }

    /// <summary>
    /// An administrator accepts the revision. The caller must then call <see cref="Article.ApplyRevision"/> in the same
    /// transaction, so the published content is replaced exactly when the revision is approved.
    /// </summary>
    /// <exception cref="InvalidTransitionException">The revision is not pending review (409).</exception>
    public void Approve(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, RevisionAction.Approve, ActorKind.Administrator);

        var timestamp = Guard.Utc(now);
        Status = RevisionLifecycle.Next(Status, RevisionAction.Approve);
        ApprovedAt = timestamp;
        UpdatedAt = timestamp;
    }

    /// <summary>An administrator sends the revision back to its author. The reason is kept on the revision review.</summary>
    /// <exception cref="InvalidTransitionException">The revision is not pending review (409).</exception>
    public void Reject(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, RevisionAction.Reject, ActorKind.Administrator);

        Status = RevisionLifecycle.Next(Status, RevisionAction.Reject);
        UpdatedAt = Guard.Utc(now);
    }

    public IReadOnlyList<RevisionAction> AllowedActions(ActorKind actor) => RevisionLifecycle.AllowedActions(Status, actor);

    private static string? Clean(string? text) => text?.Replace("\0", string.Empty, StringComparison.Ordinal);
}
