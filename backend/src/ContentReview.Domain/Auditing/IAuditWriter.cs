namespace ContentReview.Domain.Auditing;

/// <summary>
/// Records an audit event. The implementation adds the event to the current unit of work, so it is saved (or not)
/// together with the change it describes. <c>details</c> is serialized to JSON and must never contain passwords,
/// tokens or full invitation codes.
/// </summary>
public interface IAuditWriter
{
    void Record(string action, string entityType, Guid? entityId, Guid? actorUserId, object? details = null);
}

/// <summary>Audit action names written by the application.</summary>
public static class AuditActions
{
    public const string AdministratorProvisioned = "AdministratorProvisioned";
    public const string InvitationCreated = "InvitationCreated";
    public const string InvitationRevoked = "InvitationRevoked";
    public const string UserRegistered = "UserRegistered";
    public const string AdminProfileEdited = "AdminProfileEdited";
    public const string UserSuspended = "UserSuspended";
    public const string UserReactivated = "UserReactivated";
    public const string ArticleSubmitted = "ArticleSubmitted";
    public const string ArticleWithdrawn = "ArticleWithdrawn";
    public const string ArticleApproved = "ArticleApproved";
    public const string ArticleRejected = "ArticleRejected";
    public const string RevisionCreated = "RevisionCreated";
    public const string RevisionSubmitted = "RevisionSubmitted";
    public const string RevisionWithdrawn = "RevisionWithdrawn";
    public const string RevisionApproved = "RevisionApproved";
    public const string RevisionRejected = "RevisionRejected";

    /// <summary>Every action name, in the order of the table in docs/audit.md (the audit history's action filter offers these).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        AdministratorProvisioned,
        InvitationCreated,
        InvitationRevoked,
        UserRegistered,
        AdminProfileEdited,
        UserSuspended,
        UserReactivated,
        ArticleSubmitted,
        ArticleWithdrawn,
        ArticleApproved,
        ArticleRejected,
        RevisionCreated,
        RevisionSubmitted,
        RevisionWithdrawn,
        RevisionApproved,
        RevisionRejected,
    ];
}
