using System.Text.Json;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Admin;

public sealed class AuditHistoryService(ContentReviewDbContext db) : IAuditHistoryService
{
    public async Task<PagedResult<AuditEventItem>> ListAsync(AuditEventFilter filter, CancellationToken cancellationToken = default)
    {
        var events = db.AuditEvents.AsNoTracking().AsQueryable();

        if (filter.Actions.Count > 0)
        {
            var actions = filter.Actions.ToList();
            events = events.Where(e => actions.Contains(e.Action));
        }

        if (filter.ActorId is { } actorId)
        {
            events = events.Where(e => e.ActorUserId == actorId);
        }

        if (filter.From is { } from)
        {
            events = events.Where(e => e.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            events = events.Where(e => e.OccurredAt < to);
        }

        var totalCount = await events.CountAsync(cancellationToken);

        // The id breaks ties (UUIDv7 ids grow with time), so a page boundary never repeats or skips an event.
        var rows = await (
                from e in events
                join u in db.Users.AsNoTracking() on e.ActorUserId equals u.Id into actors
                from actor in actors.DefaultIfEmpty()
                orderby e.OccurredAt descending, e.Id descending
                select new Row
                {
                    Id = e.Id,
                    OccurredAt = e.OccurredAt,
                    Action = e.Action,
                    ActorId = actor == null ? null : actor.Id,
                    ActorName = actor == null ? null : actor.FullName,
                    ActorRole = actor == null ? null : actor.Role,
                    EntityType = e.EntityType,
                    EntityId = e.EntityId,
                    Details = e.Details,
                    CorrelationId = e.CorrelationId,
                })
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new AuditEventItem(
                r.Id,
                r.OccurredAt,
                r.Action,
                r.ActorId is { } id ? new AuditActor(id, r.ActorName!, r.ActorRole!.Value) : null,
                r.EntityType,
                r.EntityId,
                r.Details is null ? null : JsonDocument.Parse(r.Details).RootElement.Clone(),
                r.CorrelationId))
            .ToList();

        return new PagedResult<AuditEventItem>(items, filter.Page, filter.PageSize, totalCount);
    }

    private sealed class Row
    {
        public Guid Id { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public string Action { get; init; } = null!;
        public Guid? ActorId { get; init; }
        public string? ActorName { get; init; }
        public UserRole? ActorRole { get; init; }
        public string EntityType { get; init; } = null!;
        public Guid? EntityId { get; init; }
        public string? Details { get; init; }
        public string? CorrelationId { get; init; }
    }
}
