using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Users;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ContentReview.Domain.Exceptions.ValidationException;

namespace ContentReview.Api.Controllers;

public sealed class UserListQuery
{
    /// <summary>Any part of the full name or the email, ignoring case. <c>%</c> and <c>_</c> are ordinary characters.</summary>
    [MaxLength(100)]
    public string? Search { get; init; }

    public UserStatus? Status { get; init; }

    /// <summary><c>registeredAt</c> (default) or <c>fullName</c>.</summary>
    public UserSort Sort { get; init; } = UserSort.RegisteredAt;

    /// <summary>Defaults to <c>desc</c> for <c>registeredAt</c> (newest first) and <c>asc</c> for <c>fullName</c>.</summary>
    public SortDirection? Direction { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, PagedResult<UserListItem>.MaxPageSize)]
    public int PageSize { get; init; } = PagedResult<UserListItem>.DefaultPageSize;
}

public sealed record UpdateProfileRequest
{
    /// <summary>Display name, 2-100 characters after trimming.</summary>
    public string? FullName { get; init; }

    /// <summary>Biography, at most 1000 characters. <c>null</c> or empty clears it.</summary>
    public string? Bio { get; init; }

    /// <summary>The <c>version</c> of the author the administrator was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

public sealed record SuspendUserRequest
{
    /// <summary>Why the account is suspended, 10-500 characters after trimming.</summary>
    public string? Reason { get; init; }

    /// <summary>The <c>version</c> of the author the administrator was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

public sealed record ReactivateUserRequest
{
    /// <summary>The <c>version</c> of the author the administrator was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

/// <summary>Administrators look up author accounts. Administrators themselves are not listed.</summary>
[Route("api/admin/users")]
public sealed class AdminUsersController(
    IUserDirectoryService users, IUserManagementService management, ICurrentUser currentUser) : AdminControllerBase
{
    /// <summary>Lists author accounts.</summary>
    /// <remarks>
    /// Paged (<c>page</c>, <c>pageSize</c> up to 100). Filter with <c>search</c> and <c>status</c> (Active or Suspended);
    /// sort with <c>sort</c> (registeredAt or fullName) and <c>direction</c> (asc or desc).
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserListItem>>> List([FromQuery] UserListQuery query, CancellationToken cancellationToken)
    {
        if (query.Status is { } status && !Enum.IsDefined(status))
        {
            throw new ValidationException("status", "Status must be Active or Suspended.");
        }

        if (!Enum.IsDefined(query.Sort) || query.Direction is { } direction && !Enum.IsDefined(direction))
        {
            throw new ValidationException("sort", "Sort by registeredAt or fullName, direction asc or desc.");
        }

        var effective = query.Direction ?? (query.Sort == UserSort.FullName ? SortDirection.Asc : SortDirection.Desc);
        return Ok(await users.ListAsync(
            new UserFilter(query.Search, query.Status, query.Sort, effective, query.Page, query.PageSize), cancellationToken));
    }

    /// <summary>Returns an author's profile, status and article counts per status.</summary>
    /// <remarks>404 for an unknown id and for an administrator's id.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetails>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await users.GetAsync(id, cancellationToken));

    /// <summary>Corrects an author's display name and biography.</summary>
    /// <remarks>
    /// Only these two fields can be changed here: an email, role, status or password in the body is ignored. Works for
    /// Active and Suspended authors. 409 when <c>version</c> is stale. 404 for an administrator's id.
    /// </remarks>
    [HttpPut("{id:guid}/profile")]
    [ProducesResponseType<UserDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetails>> UpdateProfile(
        Guid id, UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await management.UpdateProfileAsync(
            id, request.FullName, request.Bio, request.Version!.Value, currentUser.UserId, cancellationToken));

    /// <summary>Suspends an author and signs the author out everywhere.</summary>
    /// <remarks>
    /// Saves the status, the reason (10-500 characters) and the time, revokes every session of the author and writes a
    /// <c>UserSuspended</c> audit event, in one transaction. The author's next request gets 401 and signing in fails.
    /// Articles, submissions and reviews are not changed. 400 for a missing or invalid reason and for an administrator's
    /// id; 404 for an unknown id; 409 when <c>version</c> is stale or the author is already suspended.
    /// </remarks>
    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType<UserDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetails>> Suspend(
        Guid id, SuspendUserRequest request, CancellationToken cancellationToken) =>
        Ok(await management.SuspendAsync(id, request.Reason, request.Version!.Value, currentUser.UserId, cancellationToken));

    /// <summary>Makes a suspended author Active again.</summary>
    /// <remarks>
    /// Clears the reason and the time and writes a <c>UserReactivated</c> audit event. Sessions revoked by the
    /// suspension stay revoked: the author signs in again. 400 for an administrator's id; 404 for an unknown id; 409 when
    /// <c>version</c> is stale or the author is not suspended.
    /// </remarks>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<UserDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetails>> Reactivate(
        Guid id, ReactivateUserRequest request, CancellationToken cancellationToken) =>
        Ok(await management.ReactivateAsync(id, request.Version!.Value, currentUser.UserId, cancellationToken));
}
