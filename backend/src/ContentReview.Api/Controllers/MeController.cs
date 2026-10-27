using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Infrastructure.Users;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

public sealed record UpdateOwnProfileRequest
{
    /// <summary>Display name, 2-100 characters after trimming.</summary>
    public string? FullName { get; init; }

    /// <summary>Biography, at most 1000 characters. <c>null</c> or empty clears it.</summary>
    public string? Bio { get; init; }

    /// <summary>The <c>version</c> of the profile the author was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

/// <summary>An author's own profile. The author is always the one the session belongs to.</summary>
[Route("api/me")]
public sealed class MeController(IAuthorProfileService profiles, ICurrentUser currentUser) : AuthorControllerBase
{
    /// <summary>Returns the signed-in author's own profile.</summary>
    /// <remarks>There is no way to ask for another author: the id comes from the session, never from the request.</remarks>
    [HttpGet("profile")]
    [ProducesResponseType<AuthorProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthorProfile>> GetProfile(CancellationToken cancellationToken) =>
        Ok(await profiles.GetAsync(currentUser.UserId, cancellationToken));

    /// <summary>Changes the signed-in author's display name and biography.</summary>
    /// <remarks>
    /// Only these two fields can be changed: an email, role, status or password in the body is ignored. 409 when
    /// <c>version</c> is stale.
    /// </remarks>
    [HttpPut("profile")]
    [ProducesResponseType<AuthorProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthorProfile>> UpdateProfile(
        UpdateOwnProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await profiles.UpdateAsync(
            currentUser.UserId, request.FullName, request.Bio, request.Version!.Value, cancellationToken));
}
