using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Users;

namespace ContentReview.Infrastructure.Reviews;

public enum ReviewQueueSort
{
    SubmittedAt,
}

public sealed record ReviewQueueFilter(
    Guid? AuthorId, Guid? CategoryId, ReviewQueueSort Sort, SortDirection Direction, int Page, int PageSize);

/// <summary>The author of a queued article, with the account status the administrator must know before deciding.</summary>
public sealed record QueueAuthor(Guid Id, string FullName, UserStatus Status);

/// <summary>What is waiting for a decision: a first submission of an article, or a revision of a published one.</summary>
public enum ReviewItemKind
{
    Article,
    Revision,
}

/// <summary>One pending submission in the queue.</summary>
/// <param name="ArticleId">The article. For a revision this is the published article it would replace.</param>
/// <param name="Kind"><c>Article</c> (open it at <c>/api/admin/articles/{articleId}</c>) or <c>Revision</c> (<c>/api/admin/revisions/{revisionId}</c>).</param>
/// <param name="RevisionId">Set only when <paramref name="Kind"/> is <c>Revision</c>.</param>
public sealed record ReviewQueueItem(
    Guid ArticleId,
    string Title,
    ArticleCategory Category,
    QueueAuthor Author,
    DateTimeOffset SubmittedAt,
    int SubmissionVersion,
    ReviewItemKind Kind,
    Guid? RevisionId);

public sealed record ReviewArticleAuthor(Guid Id, string FullName, string Email, UserStatus Status);

public sealed record ReviewerRef(Guid Id, string FullName);

/// <summary>A decision on one submission, with who made it (administrators see this; authors do not).</summary>
public sealed record ReviewHistoryEntry(
    int SubmissionVersion, ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt, ReviewerRef Reviewer);

/// <summary>
/// An article as an administrator reviews it. Title, summary, body and category are those of the current submission
/// snapshot, so the administrator sees exactly what was submitted.
/// </summary>
/// <param name="CanReview">The article is pending review and its author is Active.</param>
/// <param name="ReviewBlockedReason">Why <paramref name="CanReview"/> is false; <c>null</c> when it is true.</param>
/// <param name="Version">Concurrency token to send with the decision.</param>
public sealed record ReviewArticleDetails(
    Guid Id,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    ArticleStatus Status,
    int SubmissionVersion,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? PublishedAt,
    ReviewArticleAuthor Author,
    IReadOnlyList<ReviewHistoryEntry> ReviewHistory,
    bool CanReview,
    string? ReviewBlockedReason,
    uint Version);

/// <summary>The content that is live for readers now, shown next to a revision so the administrator sees what would change.</summary>
public sealed record PublishedContent(
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    int PublishedVersion,
    DateTimeOffset PublishedAt);

/// <summary>A decision on one submission of a revision, with who made it (administrators see this; authors do not).</summary>
public sealed record RevisionReviewHistoryEntry(
    int SubmissionVersion, ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt, ReviewerRef Reviewer);

/// <summary>
/// A revision as an administrator reviews it, next to the published content it would replace.
/// </summary>
/// <param name="Id">The revision id.</param>
/// <param name="ArticleId">The published article.</param>
/// <param name="CanReview">The revision is pending review and its author is Active.</param>
/// <param name="ReviewBlockedReason">Why <paramref name="CanReview"/> is false; <c>null</c> when it is true.</param>
/// <param name="Version">Concurrency token to send with the decision.</param>
public sealed record ReviewRevisionDetails(
    Guid Id,
    Guid ArticleId,
    int Number,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    RevisionStatus Status,
    int SubmissionVersion,
    DateTimeOffset SubmittedAt,
    PublishedContent Published,
    ReviewArticleAuthor Author,
    IReadOnlyList<RevisionReviewHistoryEntry> ReviewHistory,
    bool CanReview,
    string? ReviewBlockedReason,
    uint Version);

public sealed record AuthorLookupItem(Guid Id, string FullName);
