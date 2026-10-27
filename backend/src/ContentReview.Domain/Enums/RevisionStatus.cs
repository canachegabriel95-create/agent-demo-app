namespace ContentReview.Domain.Enums;

/// <summary>Where a revision of a published article stands. Approved means its content replaced the published version.</summary>
public enum RevisionStatus
{
    Draft,
    PendingReview,
    Rejected,
    Approved,
}
