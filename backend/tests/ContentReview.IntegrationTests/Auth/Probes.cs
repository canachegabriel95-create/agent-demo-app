using ContentReview.Api.Auth;
using ContentReview.Api.Controllers;
using ContentReview.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.IntegrationTests.Auth;

// Test-only endpoints, one per authorization group. They are added to the API under test through an application
// part (see ApiHost), so the real routes of later stories can follow the same conventions and be tested the same way.

[Route("api/admin/_probe")]
public sealed class AdminProbeController : AdminControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

[Route("api/me/_probe")]
public sealed class AuthorProbeController : AuthorControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

[Route("api/articles/_probe/{id:guid}")]
public sealed class ArticleProbeController(ContentReviewDbContext db, ICurrentUser currentUser) : AuthorControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid id)
    {
        var article = await db.Articles.OwnedBy(currentUser.UserId).FirstOrDefaultAsync(a => a.Id == id);
        return article is null ? NotFound() : Ok(new { article.Id });
    }
}

[ApiController]
[Authorize]
[Route("api/_probe/authenticated")]
public sealed class AuthenticatedProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

[ApiController]
[AllowAnonymous]
[Route("api/_probe/open")]
public sealed class OpenProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>No attribute at all: the fallback policy must still demand a signed-in user.</summary>
[ApiController]
[Route("api/_probe/unmarked")]
public sealed class UnmarkedProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
