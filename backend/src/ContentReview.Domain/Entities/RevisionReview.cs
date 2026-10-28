using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Entities;

/// <summary>An administrator's decision on one submission of a revision. Decisions are never overwritten.</summary>
public class RevisionReview
{
    private RevisionReview()
    {
    }

    public Guid Id { get; private set; }
    public Guid RevisionId { get; private set; }
    public int SubmissionVersion { get; private set; }
    public Guid ReviewerId { get; private set; }
    public ReviewDecision Decision { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    /// <summary>A rejection requires a reason (10-1000 characters, like for an article); an approval takes none.</summary>
    public static RevisionReview Create(
        Guid revisionId,
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
            ? Guard.Text(reason, nameof(reason), Review.ReasonMinLength, Review.ReasonMaxLength)
            : Guard.OptionalText(reason, nameof(reason), Review.ReasonMaxLength);

        return new RevisionReview
        {
            Id = Guid.CreateVersion7(),
            RevisionId = Guard.NotEmpty(revisionId, nameof(revisionId)),
            SubmissionVersion = submissionVersion,
            ReviewerId = Guard.NotEmpty(reviewerId, nameof(reviewerId)),
            Decision = decision,
            Reason = trimmedReason,
            DecidedAt = Guard.Utc(decidedAt),
        };
    }
}
