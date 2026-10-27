using System.Collections.Concurrent;
using System.Text.Json;
using ContentReview.Domain.Auditing;

namespace ContentReview.IntegrationTests.Infrastructure;

public sealed record RecordedAudit(string Action, string EntityType, Guid? EntityId, Guid? ActorUserId, string DetailsJson);

/// <summary>Stands in for the audit writer so tests can check exactly what would be written.</summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    private readonly ConcurrentQueue<RecordedAudit> _events = new();

    public IReadOnlyList<RecordedAudit> Events => [.. _events];

    public void Record(string action, string entityType, Guid? entityId, Guid? actorUserId, object? details = null) =>
        _events.Enqueue(new RecordedAudit(action, entityType, entityId, actorUserId, JsonSerializer.Serialize(details)));
}
