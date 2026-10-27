using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Articles;

namespace ContentReview.Infrastructure.Revisions;

/// <summary>The decision on the most recent submission of the revision that was reviewed.</summary>
public sealed record LatestRevisionReview(ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);

/// <summary>
/// A revision of a published article as its author sees it. Editing it never changes what readers see: that only happens
/// when an administrator approves it.
/// </summary>
/// <param name="AllowedActions">What the author may do now: <c>edit</c>, <c>submit</c>, <c>withdraw</c>.</param>
/// <param name="Version">Concurrency token: send it back when changing the revision.</param>
public sealed record RevisionDto(
    Guid Id,
    Guid ArticleId,
    int Number,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    RevisionStatus Status,
    int SubmissionVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    LatestRevisionReview? LatestReview,
    IReadOnlyList<string> AllowedActions,
    uint Version);

/// <summary>One administrator decision on one submission of one revision, as the author sees it (never who decided).</summary>
public sealed record RevisionReviewDto(
    int RevisionNumber, int SubmissionVersion, ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);
