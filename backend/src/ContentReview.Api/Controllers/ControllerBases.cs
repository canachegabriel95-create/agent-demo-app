using ContentReview.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

/// <summary>
/// Base for administrator endpoints (route prefix <c>api/admin</c>). An Author token gets 403, no token gets 401.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
public abstract class AdminControllerBase : ControllerBase;

/// <summary>
/// Base for author endpoints (<c>api/me</c>, <c>api/articles</c>, <c>api/feed</c>). An Administrator token gets 403,
/// no token gets 401. Resources must be filtered with <see cref="AuthorOwnership.OwnedBy"/>.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.AuthorOnly)]
public abstract class AuthorControllerBase : ControllerBase;
