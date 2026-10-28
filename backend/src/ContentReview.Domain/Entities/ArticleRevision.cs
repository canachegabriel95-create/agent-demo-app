using ContentReview.Domain.Articles;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.Domain.Entities;

/// <summary>
/// A proposed new version of a Published article. The published version stays untouched (and visible) until an
/// administrator approves the revision; then its content replaces the published content. An article has at most one
/// open revision. While a revision is PendingReview its content is read-only, so what the administrator reviews is
/// what the author submitted.
/// </summary>
public class ArticleRevision
{
    private ArticleRevision()
    {
    }

    public Guid Id { get; private set; }
    public Guid ArticleId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public RevisionStatus Status { get; private set; }

    /// <summary>Number of times this revision has been submitted for review (0 while never submitted).</summary>
    public int SubmissionVersion { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    /// <summary>Whether the revision still blocks a new one: everything but Approved.</summary>
    public bool IsOpen => Status != RevisionStatus.Approved;

    /// <summary>Starts a revision as a Draft copy of the article's current published content.</summary>
    /// <exception cref="InvalidTransitionException">The article is not Published.</exception>
    public static ArticleRevision CreateFrom(Article published, DateTimeOffset now)
    {
        ArticleLifecycle.EnsureCanPerform(published.Status, ArticleAction.Revise, ActorKind.Owner);
        var timestamp = Guard.Utc(now);

        return new ArticleRevision
        {
            Id = Guid.CreateVersion7(),
            ArticleId = published.Id,
            CategoryId = published.CategoryId,
            Title = published.Title,
            Summary = published.Summary,
            Body = published.Body,
            Status = RevisionStatus.Draft,
            SubmissionVersion = 0,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }

    /// <summary>Replaces the content; the draft limits apply, like for a draft article.</summary>
    /// <exception cref="InvalidTransitionException">The revision is not editable in its status.</exception>
    public void UpdateContent(string title, string? summary, string? body, Guid? categoryId, DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, ArticleAction.Edit, ActorKind.Owner);

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

    /// <exception cref="InvalidTransitionException">The revision is not a Draft or Rejected.</exception>
    public void EnsureCanDelete() => RevisionLifecycle.EnsureCanPerform(Status, ArticleAction.Delete, ActorKind.Owner);

    /// <summary>Sends the revision to review (also a resubmission of a Rejected one); the submission rules apply.</summary>
    /// <exception cref="InvalidTransitionException">The revision cannot be submitted in its status.</exception>
    /// <exception cref="ValidationException">The content does not meet the submission rules; nothing changes.</exception>
    public void Submit(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, ArticleAction.Submit, ActorKind.Owner);

        var errors = ArticleSubmissionValidator.Validate(Title, Summary, Body, CategoryId);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var timestamp = Guard.Utc(now);
        Status = RevisionLifecycle.Next(Status, ArticleAction.Submit)!.Value;
        SubmissionVersion++;
        SubmittedAt = timestamp;
        UpdatedAt = timestamp;
    }

    /// <exception cref="InvalidTransitionException">The revision is not pending review.</exception>
    public void Withdraw(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, ArticleAction.Withdraw, ActorKind.Owner);
        Status = RevisionLifecycle.Next(Status, ArticleAction.Withdraw)!.Value;
        UpdatedAt = Guard.Utc(now);
    }

    /// <summary>
    /// An administrator accepts the revision. The caller replaces the published content in the same transaction
    /// (<see cref="Article.ReplaceContentWith"/>).
    /// </summary>
    /// <exception cref="InvalidTransitionException">The revision is not pending review.</exception>
    public void Approve(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, ArticleAction.Approve, ActorKind.Administrator);
        Status = RevisionLifecycle.Next(Status, ArticleAction.Approve)!.Value;
        UpdatedAt = Guard.Utc(now);
    }

    /// <summary>An administrator sends the revision back; the reason is kept on the review.</summary>
    /// <exception cref="InvalidTransitionException">The revision is not pending review.</exception>
    public void Reject(DateTimeOffset now)
    {
        RevisionLifecycle.EnsureCanPerform(Status, ArticleAction.Reject, ActorKind.Administrator);
        Status = RevisionLifecycle.Next(Status, ArticleAction.Reject)!.Value;
        UpdatedAt = Guard.Utc(now);
    }

    public IReadOnlyList<ArticleAction> AllowedActions(ActorKind actor) => RevisionLifecycle.AllowedActions(Status, actor);

    private static string? Clean(string? text) => text?.Replace("\0", string.Empty, StringComparison.Ordinal);
}
