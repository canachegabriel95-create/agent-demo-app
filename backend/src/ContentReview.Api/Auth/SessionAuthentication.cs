using System.Security.Claims;
using System.Text.Encodings.Web;
using ContentReview.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ContentReview.Api.Auth;

/// <summary>Names shared by the authentication handler, the controllers and the tests.</summary>
public static class SessionAuthentication
{
    public const string Scheme = "Session";

    public const string SubjectClaim = "sub";
    public const string RoleClaim = "role";
    public const string SessionIdClaim = "sid";
    public const string NameClaim = "name";
    public const string EmailClaim = "email";
}

/// <summary>
/// Authenticates <c>Authorization: Bearer &lt;token&gt;</c> against <see cref="ISessionService"/> on every request, so
/// a revoked session, an expired session or a suspended user is rejected immediately.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionService sessions,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return AuthenticateResult.NoResult();
        }

        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Unsupported authorization scheme.");
        }

        var session = await sessions.ValidateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (session is null)
        {
            // The reason is deliberately not distinguished (unknown, revoked, expired, suspended) and the token is never logged.
            return AuthenticateResult.Fail("Invalid session.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(SessionAuthentication.SubjectClaim, session.UserId.ToString()),
                new Claim(SessionAuthentication.SessionIdClaim, session.SessionId.ToString()),
                new Claim(SessionAuthentication.RoleClaim, session.Role.ToString()),
                new Claim(SessionAuthentication.NameClaim, session.FullName),
                new Claim(SessionAuthentication.EmailClaim, session.Email),
            ],
            SessionAuthentication.Scheme,
            nameType: SessionAuthentication.NameClaim,
            roleType: SessionAuthentication.RoleClaim);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SessionAuthentication.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await WriteProblemAsync(StatusCodes.Status401Unauthorized, "Authentication is required.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return WriteProblemAsync(StatusCodes.Status403Forbidden, "You do not have permission to perform this action.");
    }

    private async Task WriteProblemAsync(int status, string title) =>
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = { Status = status, Title = title },
        });
}
