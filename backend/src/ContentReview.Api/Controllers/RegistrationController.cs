using System.ComponentModel.DataAnnotations;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Invitations;
using ContentReview.Infrastructure.Registration;
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

/// <summary>
/// The registration form. There is deliberately no <c>role</c>: every account created here is an Author, and a
/// <c>role</c> sent anyway is ignored. The field rules are in docs/business-rules.md (Accounts).
/// </summary>
public sealed record RegisterRequest
{
    public string? InvitationCode { get; init; }

    public string? FullName { get; init; }

    public string? Email { get; init; }

    public string? Password { get; init; }

    public string? ConfirmPassword { get; init; }
}

public sealed record RegisterResponse(Guid Id, string Email, string FullName);

/// <summary>Anonymous endpoints used by the Author Portal registration form.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/registration")]
public sealed class RegistrationController(IInvitationRedemptionService redemption, IRegistrationService registration)
    : ControllerBase
{
    /// <summary>Creates an Author account with an invitation code.</summary>
    /// <remarks>
    /// The invitation is used up in the same transaction that creates the account, so a failure leaves no account and
    /// the invitation Available. No session is created: sign in afterwards. The password and email rules are in
    /// docs/business-rules.md, section "Accounts".
    /// 400 lists the invalid fields; an unusable invitation is reported on <c>invitationCode</c> (or <c>email</c> when
    /// the address does not match it). 409 means an account with this email already exists.
    /// Shares the rate limit of the login endpoint.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(AuthController.LoginRateLimitPolicy)]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await registration.RegisterAsync(
            new RegistrationInput(request.InvitationCode, request.FullName, request.Email, request.Password, request.ConfirmPassword),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(user.Id, user.Email, user.FullName));
    }

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
