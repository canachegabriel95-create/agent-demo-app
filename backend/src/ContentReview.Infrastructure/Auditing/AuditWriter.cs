using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Entities;
using ContentReview.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace ContentReview.Infrastructure.Auditing;

/// <summary>
/// Adds the audit event to the current <see cref="ContentReviewDbContext"/> and nothing more: it is saved by the same
/// <c>SaveChanges</c> (so in the same transaction) as the change it describes, and lost with it on a rollback.
/// </summary>
public sealed class AuditWriter(
    ContentReviewDbContext db,
    TimeProvider timeProvider,
    ICorrelationIdSource correlationId,
    IOptions<AuditOptions> options) : IAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public void Record(string action, string entityType, Guid? entityId, Guid? actorUserId, object? details = null)
    {
        string? json = null;
        if (details is not null)
        {
            var node = JsonSerializer.SerializeToNode(details, Json);
            json = AuditDetailsSanitizer.Sanitize(node, options.Value.ThrowOnSensitiveDetails ?? false)?.ToJsonString(Json);
        }

        db.AuditEvents.Add(AuditEvent.Create(
            action,
            entityType,
            timeProvider.GetUtcNow(),
            actorUserId,
            entityId,
            json,
            correlationId.CorrelationId));
    }
}
