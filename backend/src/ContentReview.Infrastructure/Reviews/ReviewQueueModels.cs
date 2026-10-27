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

/// <summary>One pending submission in the queue.</summary>
public sealed record ReviewQueueItem(
    Guid ArticleId,
    string Title,
    ArticleCategory Category,
    QueueAuthor Author,
    DateTimeOffset SubmittedAt,
    int SubmissionVersion);

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
