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

/// <summary>Administrators look up author accounts. Administrators themselves are not listed.</summary>
[Route("api/admin/users")]
public sealed class AdminUsersController(IUserDirectoryService users) : AdminControllerBase
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
}
