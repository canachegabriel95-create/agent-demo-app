using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Articles;

namespace ContentReview.Infrastructure.Revisions;

/// <summary>
/// One administrator decision on one submission of a revision, as the author sees it. The reviewer is deliberately not
/// part of it.
/// </summary>
public sealed record RevisionReviewDto(int SubmissionVersion, ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);

/// <summary>A revision of a published article as its author sees it.</summary>
/// <param name="Status">Draft, PendingReview, Rejected or Approved (final).</param>
/// <param name="Reviews">The decisions on the revision's submissions, newest first.</param>
/// <param name="AllowedActions">What the author may do now: <c>edit</c>, <c>delete</c>, <c>submit</c>, <c>withdraw</c>.</param>
/// <param name="Version">Concurrency token: send it back when changing, submitting, withdrawing or deleting the revision.</param>
public sealed record RevisionDto(
    Guid Id,
    Guid ArticleId,
    string Title,
    string Summary,
    string Body,
    ArticleCategory? Category,
    RevisionStatus Status,
    int SubmissionVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    LatestReview? LatestReview,
    IReadOnlyList<RevisionReviewDto> Reviews,
    IReadOnlyList<string> AllowedActions,
    uint Version);
