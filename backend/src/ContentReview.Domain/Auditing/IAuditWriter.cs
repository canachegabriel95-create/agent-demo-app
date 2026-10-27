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
    public const string InvitationCreated = "InvitationCreated";
    public const string InvitationRevoked = "InvitationRevoked";
    public const string UserRegistered = "UserRegistered";
    public const string AdminProfileEdited = "AdminProfileEdited";
    public const string UserSuspended = "UserSuspended";
    public const string UserReactivated = "UserReactivated";
}
