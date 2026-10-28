using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Articles;

public sealed record ArticleCategory(Guid Id, string Name);

/// <summary>The decision on the most recent submission that was reviewed.</summary>
public sealed record LatestReview(ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);

/// <summary>
/// One administrator decision on one submission, as the author sees it. The reviewer is deliberately not part of it.
/// </summary>
public sealed record ArticleReviewDto(int SubmissionVersion, ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);

/// <summary>The open revision of a published article, as shown next to it.</summary>
public sealed record RevisionSummary(Guid Id, RevisionStatus Status, DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt);

/// <summary>An article as its author sees it.</summary>
/// <param name="AllowedActions">What the author may do now, from the lifecycle state machine: <c>edit</c>, <c>delete</c>, <c>submit</c>, <c>withdraw</c>, <c>revise</c> (a published article without an open revision).</param>
/// <param name="OpenRevision">The revision in progress (Draft, PendingReview or Rejected) of a published article; <c>null</c> otherwise.</param>
/// <param name="PublishedVersion">0 until the article is published; 1 after the first publication, plus one for each approved revision.</param>
/// <param name="Version">Concurrency token: send it back when changing or deleting the article.</param>
public sealed record ArticleDto(
    Guid Id,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    ArticleStatus Status,
    int SubmissionVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? PublishedAt,
    LatestReview? LatestReview,
    IReadOnlyList<string> AllowedActions,
    RevisionSummary? OpenRevision,
    int PublishedVersion,
    uint Version);
