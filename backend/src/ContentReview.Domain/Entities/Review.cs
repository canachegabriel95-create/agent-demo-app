using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Entities;

/// <summary>An administrator's decision on one submission of an article. Decisions are never overwritten.</summary>
public class Review
{
    public const int ReasonMinLength = 10;
    public const int ReasonMaxLength = 1000;

    private Review()
    {
    }

    public Guid Id { get; private set; }
    public Guid ArticleId { get; private set; }
    public int SubmissionVersion { get; private set; }
    public Guid ReviewerId { get; private set; }
    public ReviewDecision Decision { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    /// <summary>A rejection requires a reason (10-1000 characters); an approval takes none.</summary>
    public static Review Create(
        Guid articleId,
        int submissionVersion,
        Guid reviewerId,
        ReviewDecision decision,
        string? reason,
        DateTimeOffset decidedAt)
    {
        if (submissionVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(submissionVersion), submissionVersion, "Submission version starts at 1.");
        }

        Guard.Defined(decision, nameof(decision));
        var trimmedReason = decision == ReviewDecision.Rejected
            ? Guard.Text(reason, nameof(reason), ReasonMinLength, ReasonMaxLength)
            : Guard.OptionalText(reason, nameof(reason), ReasonMaxLength);

        return new Review
        {
            Id = Guid.CreateVersion7(),
            ArticleId = Guard.NotEmpty(articleId, nameof(articleId)),
            SubmissionVersion = submissionVersion,
            ReviewerId = Guard.NotEmpty(reviewerId, nameof(reviewerId)),
            Decision = decision,
            Reason = trimmedReason,
            DecidedAt = Guard.Utc(decidedAt),
        };
    }
}
