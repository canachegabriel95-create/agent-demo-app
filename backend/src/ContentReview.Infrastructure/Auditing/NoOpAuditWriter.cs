using ContentReview.Domain.Auditing;

namespace ContentReview.Infrastructure.Auditing;

/// <summary>Placeholder until the audit history story (F13.1) provides the real writer; it records nothing.</summary>
public sealed class NoOpAuditWriter : IAuditWriter
{
    public void Record(string action, string entityType, Guid? entityId, Guid? actorUserId, object? details = null)
    {
    }
}
