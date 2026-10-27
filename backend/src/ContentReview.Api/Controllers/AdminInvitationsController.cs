using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ValidationException = ContentReview.Domain.Exceptions.ValidationException;
using ContentReview.Infrastructure.Invitations;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

public sealed record CreateInvitationRequest
{
    [Required]
    public string? Email { get; init; }

    /// <summary>Days until the invitation expires, 1-30. Defaults to 7.</summary>
    public int? ValidForDays { get; init; }
}

public sealed record RevokeInvitationRequest
{
    /// <summary>The <c>version</c> of the invitation the administrator was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

public sealed class InvitationListQuery
{
    /// <summary>Case-insensitive part of the invited email.</summary>
    public string? Email { get; init; }

    public InvitationStatus? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, PagedResult<InvitationDto>.MaxPageSize)]
    public int PageSize { get; init; } = PagedResult<InvitationDto>.DefaultPageSize;
}

public sealed record UserRefDto(Guid Id, string FullName);

public sealed record InvitationDto(
    Guid Id,
    string Code,
    string Email,
    InvitationStatus Status,
    DateTimeOffset CreatedAt,
    UserRefDto CreatedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    UserRefDto? RevokedBy,
    DateTimeOffset? UsedAt,
    UserRefDto? UsedBy,
    uint Version)
{
    public static InvitationDto From(InvitationView view) => new(
        view.Id,
        view.Code,
        view.Email,
        view.Status,
        view.CreatedAt,
        Map(view.CreatedBy)!,
        view.ExpiresAt,
        view.RevokedAt,
        Map(view.RevokedBy),
        view.UsedAt,
        Map(view.UsedBy),
        view.Version);

    private static UserRefDto? Map(UserRef? user) => user is null ? null : new UserRefDto(user.Id, user.FullName);
}

/// <summary>Administrators generate, list, filter and revoke registration invitation codes.</summary>
[Route("api/admin/invitations")]
public sealed class AdminInvitationsController(IInvitationService invitations, ICurrentUser currentUser) : AdminControllerBase
{
    /// <summary>Creates an invitation for an email address.</summary>
    /// <remarks>
    /// The code is returned in full so the administrator can copy and share it. Several Available invitations
    /// may exist for the same email. An email that already belongs to a user gets 409.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<InvitationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvitationDto>> Create(CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var view = await invitations.CreateAsync(request.Email, request.ValidForDays, currentUser.UserId, cancellationToken);
        var dto = InvitationDto.From(view);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>Lists invitations, newest first.</summary>
    /// <remarks>
    /// <c>status</c> is Available, Used, Expired or Revoked; Expired is derived from <c>expiresAt</c>.
    /// <c>email</c> matches any part of the invited address, ignoring case.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<InvitationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<InvitationDto>>> List([FromQuery] InvitationListQuery query, CancellationToken cancellationToken)
    {
        if (query.Status is { } status && !Enum.IsDefined(status))
        {
            throw new ValidationException("status", "Status must be Available, Used, Expired or Revoked.");
        }

        var page = await invitations.ListAsync(
            new InvitationFilter(query.Email, query.Status, query.Page, query.PageSize), cancellationToken);
        return Ok(new PagedResult<InvitationDto>(page.Items.Select(InvitationDto.From).ToList(), page.Page, page.PageSize, page.TotalCount));
    }

    /// <summary>Returns one invitation.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<InvitationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvitationDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(InvitationDto.From(await invitations.GetAsync(id, cancellationToken)));

    /// <summary>Revokes an Available invitation.</summary>
    /// <remarks>409 when the invitation is Used, Expired or already Revoked, or when <c>version</c> is stale.</remarks>
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType<InvitationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvitationDto>> Revoke(Guid id, RevokeInvitationRequest request, CancellationToken cancellationToken) =>
        Ok(InvitationDto.From(await invitations.RevokeAsync(id, request.Version!.Value, currentUser.UserId, cancellationToken)));
}
