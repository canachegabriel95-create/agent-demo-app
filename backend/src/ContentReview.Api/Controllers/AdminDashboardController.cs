using System.ComponentModel.DataAnnotations;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Common;
using ContentReview.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ContentReview.Domain.Exceptions.ValidationException;

namespace ContentReview.Api.Controllers;

public sealed class AuditEventsQuery
{
    /// <summary>One or more action names (repeat the parameter): <c>?action=UserSuspended&amp;action=UserReactivated</c>.</summary>
    [FromQuery(Name = "action")]
    public string[]? Action { get; init; }

    /// <summary>Only events by this user.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>Events at or after this time (inclusive), ISO 8601 with an offset, e.g. <c>2026-10-27T00:00:00Z</c>.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Events before this time (exclusive), ISO 8601 with an offset. Not before <c>from</c>.</summary>
    public DateTimeOffset? To { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, PagedResult<AuditEventItem>.MaxPageSize)]
    public int PageSize { get; init; } = PagedResult<AuditEventItem>.DefaultPageSize;
}

/// <summary>The operational overview for administrators.</summary>
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController(IAdminDashboardService dashboard) : AdminControllerBase
{
    /// <summary>Counts for the administrator dashboard.</summary>
    /// <remarks>
    /// Authors only for the user counts. <c>availableInvitations</c> are those not used, not revoked and not expired.
    /// <c>pendingReviews</c> is the number of articles waiting for a decision.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<AdminDashboard>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminDashboard>> Get(CancellationToken cancellationToken) =>
        Ok(await dashboard.GetAsync(cancellationToken));
}

/// <summary>The audit history. Read-only: there is no way to write, change or delete an event.</summary>
[Route("api/admin/audit-events")]
public sealed class AuditEventsController(IAuditHistoryService history) : AdminControllerBase
{
    /// <summary>Lists audit events, newest first.</summary>
    /// <remarks>
    /// Filter with <c>action</c> (one or more of the names from <c>/actions</c>), <c>actorId</c>, <c>from</c> (inclusive) and
    /// <c>to</c> (exclusive); paged with <c>page</c> and <c>pageSize</c> (up to 100). 400 for an unknown action, an
    /// unreadable date, or <c>from</c> after <c>to</c>. <c>actor</c> is <c>null</c> for events written by the system.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditEventItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditEventItem>>> List(
        [FromQuery] AuditEventsQuery query, CancellationToken cancellationToken)
    {
        var actions = (query.Action ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct().ToList();
        var unknown = actions.FirstOrDefault(a => !AuditActions.All.Contains(a));
        if (unknown is not null)
        {
            throw new ValidationException("action", $"Unknown action '{unknown}'. See /api/admin/audit-events/actions.");
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new ValidationException("to", "The end of the range must not be before its start.");
        }

        return Ok(await history.ListAsync(
            new AuditEventFilter(actions, query.ActorId, query.From, query.To, query.Page, query.PageSize), cancellationToken));
    }

    /// <summary>Lists the action names an audit event can have, for a filter.</summary>
    [HttpGet("actions")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public ActionResult<IReadOnlyList<string>> Actions() => Ok(AuditActions.All);
}
