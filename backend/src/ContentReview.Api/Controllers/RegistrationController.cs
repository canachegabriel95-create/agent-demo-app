using System.ComponentModel.DataAnnotations;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ContentReview.Api.Controllers;

public sealed record ValidateInvitationRequest
{
    /// <summary>The invitation code, in any case, with or without dashes.</summary>
    [Required]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(EmailAddress.MaxLength)]
    public string Email { get; init; } = string.Empty;
}

public sealed record ValidateInvitationResponse(bool Valid);

/// <summary>Anonymous endpoints used by the Author Portal registration form.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/registration")]
public sealed class RegistrationController(IInvitationRedemptionService redemption) : ControllerBase
{
    /// <summary>Checks an invitation code and email without using the code up.</summary>
    /// <remarks>
    /// Gives the person early feedback before they fill in the rest of the form. The registration itself checks
    /// again and consumes the code. 400 carries the reason in <c>title</c> and as a stable <c>reason</c> value:
    /// NotFound, Expired, Revoked, AlreadyUsed or EmailMismatch. Shares the rate limit of the login endpoint.
    /// </remarks>
    [HttpPost("validate-invitation")]
    [EnableRateLimiting(AuthController.LoginRateLimitPolicy)]
    [ProducesResponseType<ValidateInvitationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ValidateInvitationResponse>> ValidateInvitation(
        ValidateInvitationRequest request, CancellationToken cancellationToken)
    {
        var result = await redemption.ValidateAsync(request.Code, request.Email, cancellationToken);
        if (result == InvitationValidationResult.Valid)
        {
            return Ok(new ValidateInvitationResponse(true));
        }

        var details = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status400BadRequest, title: result.Message());
        details.Extensions["reason"] = result.ToString();
        return BadRequest(details);
    }
}
