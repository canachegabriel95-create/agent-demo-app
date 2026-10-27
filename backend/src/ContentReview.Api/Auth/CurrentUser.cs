using System.Security.Claims;
using ContentReview.Domain.Enums;

namespace ContentReview.Api.Auth;

/// <summary>The authenticated caller, read from the claims the session handler built for this request.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    UserRole Role { get; }

    Guid SessionId { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => Guid.Parse(Claim(SessionAuthentication.SubjectClaim));

    public UserRole Role => Enum.Parse<UserRole>(Claim(SessionAuthentication.RoleClaim));

    public Guid SessionId => Guid.Parse(Claim(SessionAuthentication.SessionIdClaim));

    private string Claim(string type) =>
        accessor.HttpContext?.User.FindFirstValue(type)
        ?? throw new InvalidOperationException(
            $"There is no authenticated user (claim '{type}' is missing). Only use ICurrentUser on authorized endpoints.");
}
