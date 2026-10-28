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

/// <summary>What is waiting for review: a first submission of an article, or a revision of a published article.</summary>
public enum ReviewItemKind
{
    Submission,
    Revision,
}

/// <summary>One pending item in the queue: a submission or a revision.</summary>
/// <param name="ArticleId">The article. For a revision this is the published article being revised.</param>
/// <param name="Kind"><c>Submission</c> opens at <c>GET /api/admin/articles/{articleId}</c>, <c>Revision</c> at <c>GET /api/admin/revisions/{revisionId}</c>.</param>
/// <param name="RevisionId">Set for a revision; <c>null</c> for a submission.</param>
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

public sealed record AuthorLookupItem(Guid Id, string FullName);

/// <summary>The version of an article that readers see now, shown next to the revision that would replace it.</summary>
public sealed record PublishedVersionView(
    int VersionNumber,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    DateTimeOffset PublishedAt);

/// <summary>
/// A revision as an administrator reviews it: its content, the published version it would replace, the author and
/// the decisions made on earlier submissions of this revision.
/// </summary>
/// <param name="CanReview">The revision is pending review and its author is Active.</param>
/// <param name="ReviewBlockedReason">Why <paramref name="CanReview"/> is false; <c>null</c> when it is true.</param>
/// <param name="Version">Concurrency token to send with the decision.</param>
public sealed record ReviewRevisionDetails(
    Guid Id,
    Guid ArticleId,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    RevisionStatus Status,
    int SubmissionVersion,
    DateTimeOffset? SubmittedAt,
    ReviewArticleAuthor Author,
    PublishedVersionView Published,
    IReadOnlyList<ReviewHistoryEntry> ReviewHistory,
    bool CanReview,
    string? ReviewBlockedReason,
    uint Version);
