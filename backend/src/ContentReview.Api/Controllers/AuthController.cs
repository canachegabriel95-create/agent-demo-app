using System.Security.Claims;
using ContentReview.Api.Auth;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ContentReview.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    CredentialsValidator credentials,
    ISessionService sessions) : ControllerBase
{
    public const string InvalidCredentialsTitle = "Invalid email or password.";
    public const string LoginRateLimitPolicy = "login";

    /// <summary>Signs in to a portal and returns an opaque bearer token.</summary>
    /// <remarks>
    /// A wrong password, an unknown email, a suspended account and a role that does not match the portal all
    /// return the same 401, so the response does not reveal whether an account exists.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await credentials.ValidateAsync(
            request.Email, request.Password, request.Portal!.Value, cancellationToken);
        if (user is null)
        {
            return Problem(title: InvalidCredentialsTitle, statusCode: StatusCodes.Status401Unauthorized);
        }

        var session = await sessions.CreateAsync(user.Id, cancellationToken);
        return Ok(new LoginResponse(session.Token, session.ExpiresAt, UserSummary.From(user)));
    }

    /// <summary>Ends the current session on the server; the token stops working immediately.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(Guid.Parse(User.FindFirstValue(SessionAuthentication.SessionIdClaim)!), cancellationToken);
        return NoContent();
    }

    /// <summary>Returns the user of the current session.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserResponse> Me() =>
        Ok(new CurrentUserResponse(
            Guid.Parse(User.FindFirstValue(SessionAuthentication.SubjectClaim)!),
            User.FindFirstValue(SessionAuthentication.NameClaim)!,
            User.FindFirstValue(SessionAuthentication.EmailClaim)!,
            Enum.Parse<UserRole>(User.FindFirstValue(SessionAuthentication.RoleClaim)!),
            UserStatus.Active));
}
