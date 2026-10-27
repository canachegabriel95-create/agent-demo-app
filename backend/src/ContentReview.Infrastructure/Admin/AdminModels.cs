using System.Text.Json;
using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Admin;

/// <summary>The numbers on the administrator's dashboard. Only authors count as users.</summary>
/// <param name="AvailableInvitations">Not used, not revoked and not expired (the derived Available status).</param>
/// <param name="PendingReviews">Articles waiting for a decision.</param>
public sealed record AdminDashboard(
    int ActiveAuthors,
    int SuspendedAuthors,
    int AvailableInvitations,
    int PendingReviews,
    int PublishedArticles);

public sealed record AuditEventFilter(
    IReadOnlyList<string> Actions,
    Guid? ActorId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public sealed record AuditActor(Guid Id, string FullName, UserRole Role);

/// <summary>One audit event as the administrator sees it.</summary>
/// <param name="Actor"><c>null</c> for an event written by the system (e.g. the administrator provisioning script).</param>
/// <param name="Details">Non-sensitive facts about the action, as recorded.</param>
public sealed record AuditEventItem(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    AuditActor? Actor,
    string EntityType,
    Guid? EntityId,
    JsonElement? Details,
    string? CorrelationId);
