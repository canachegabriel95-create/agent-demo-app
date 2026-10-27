using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Articles;

public sealed record ArticleCategory(Guid Id, string Name);

/// <summary>The decision on the most recent submission that was reviewed.</summary>
public sealed record LatestReview(ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);

/// <summary>
/// One administrator decision on one submission, as the author sees it. The reviewer is deliberately not part of it.
/// </summary>
public sealed record ArticleReviewDto(int SubmissionVersion, ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);

/// <summary>The revision of a published article that is being edited or reviewed (at most one per article).</summary>
public sealed record OpenRevisionSummary(Guid Id, int Number, RevisionStatus Status, DateTimeOffset UpdatedAt);

/// <summary>An article as its author sees it.</summary>
/// <param name="AllowedActions">What the author may do now, from the lifecycle state machine: <c>edit</c>, <c>delete</c>, <c>submit</c>, <c>withdraw</c>, and <c>revise</c> (start a revision of a published article that has none open).</param>
/// <param name="Version">Concurrency token: send it back when changing or deleting the article.</param>
/// <param name="PublishedVersion">Which published version is live: 0 until published, plus one per approved revision.</param>
/// <param name="OpenRevision">The revision in progress, or <c>null</c>. The article itself stays unchanged until it is approved.</param>
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
    uint Version,
    int PublishedVersion,
    OpenRevisionSummary? OpenRevision);
