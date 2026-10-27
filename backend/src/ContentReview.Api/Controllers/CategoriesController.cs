using ContentReview.Infrastructure.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

/// <summary>The article categories, for the editor and the filters of both portals.</summary>
[ApiController]
[Route("api/categories")]
[Authorize]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    /// <summary>Lists the categories in display order.</summary>
    /// <remarks>
    /// Any signed-in user, administrator or author. The list is fixed in this version (no endpoint creates, edits or
    /// deletes categories), so the response may be cached by the browser for five minutes
    /// (<c>Cache-Control: private, max-age=300</c>).
    /// </remarks>
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.Client, Duration = 300)]
    [ProducesResponseType<IReadOnlyList<CategoryItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CategoryItem>>> List(CancellationToken cancellationToken) =>
        Ok(await categories.ListAsync(cancellationToken));
}
