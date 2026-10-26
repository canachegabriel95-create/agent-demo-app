using System.Text.Json;
using ContentReview.Domain.Common;

namespace ContentReview.Domain.Entities;

/// <summary>
/// Append-only audit record. <see cref="Details"/> is JSON text and must never contain passwords,
/// tokens or full invitation codes.
/// </summary>
public class AuditEvent
{
    public const int NameMaxLength = 100;

    private AuditEvent()
    {
    }

    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public Guid? EntityId { get; private set; }
    public string? Details { get; private set; }
    public string? CorrelationId { get; private set; }

    public static AuditEvent Create(
        string action,
        string entityType,
        DateTimeOffset occurredAt,
        Guid? actorUserId = null,
        Guid? entityId = null,
        string? detailsJson = null,
        string? correlationId = null)
    {
        if (detailsJson is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(detailsJson);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("details must be valid JSON.", nameof(detailsJson), ex);
            }
        }

        return new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = Guard.Utc(occurredAt),
            ActorUserId = actorUserId == Guid.Empty ? throw new ArgumentException("actorUserId must not be empty.", nameof(actorUserId)) : actorUserId,
            Action = Guard.Text(action, nameof(action), 1, NameMaxLength),
            EntityType = Guard.Text(entityType, nameof(entityType), 1, NameMaxLength),
            EntityId = entityId,
            Details = detailsJson,
            CorrelationId = Guard.OptionalText(correlationId, nameof(correlationId), NameMaxLength),
        };
    }
}
