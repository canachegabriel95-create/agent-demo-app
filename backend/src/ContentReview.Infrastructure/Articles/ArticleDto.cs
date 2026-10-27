using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Articles;

public sealed record ArticleCategory(Guid Id, string Name);

/// <summary>The decision on the most recent submission that was reviewed.</summary>
public sealed record LatestReview(ReviewDecision Decision, string? Reason, DateTimeOffset DecidedAt);

/// <summary>An article as its author sees it.</summary>
/// <param name="AllowedActions">What the author may do now, from the lifecycle state machine: <c>edit</c>, <c>delete</c>, <c>submit</c>, <c>withdraw</c>.</param>
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
    uint Version);
