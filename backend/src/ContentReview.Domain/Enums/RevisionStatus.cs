namespace ContentReview.Domain.Enums;

/// <summary>
/// Where a revision of a published article is in review. A revision is open while it is a Draft, PendingReview or
/// Rejected; Approved is final (its content replaced the published version).
/// </summary>
public enum RevisionStatus
{
    Draft,
    PendingReview,
    Rejected,
    Approved,
}
