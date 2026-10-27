using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Infrastructure.Articles;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

public sealed record CreateArticleRequest
{
    /// <summary>Required, 1-150 characters after trimming.</summary>
    public string? Title { get; init; }

    /// <summary>At most 500 characters.</summary>
    public string? Summary { get; init; }

    /// <summary>Plain text, at most 20000 characters. It is never rendered as HTML.</summary>
    public string? Body { get; init; }

    /// <summary>Optional here; must be one of <c>GET /api/categories</c> when given.</summary>
    public Guid? CategoryId { get; init; }
}

public sealed record UpdateArticleRequest
{
    /// <summary>Required, 1-150 characters after trimming.</summary>
    public string? Title { get; init; }

    public string? Summary { get; init; }

    public string? Body { get; init; }

    /// <summary><c>null</c> clears the category.</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>The <c>version</c> of the article the author was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

/// <summary>An author's own articles. Somebody else's article is a 404, exactly like one that does not exist.</summary>
[Route("api/articles")]
public sealed class ArticlesController(IArticleService articles, ICurrentUser currentUser) : AuthorControllerBase
{
    /// <summary>Creates a draft.</summary>
    /// <remarks>Only the title is required. The draft is private to its author and kept until the author deletes it.</remarks>
    [HttpPost]
    [ProducesResponseType<ArticleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ArticleDto>> Create(CreateArticleRequest request, CancellationToken cancellationToken)
    {
        var created = await articles.CreateAsync(
            currentUser.UserId, request.Title, request.Summary, request.Body, request.CategoryId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Returns one of the caller's articles with the actions the lifecycle allows now.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ArticleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArticleDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await articles.GetAsync(currentUser.UserId, id, cancellationToken));

    /// <summary>Replaces the content of a Draft or Rejected article.</summary>
    /// <remarks>
    /// 409 when the article cannot be edited in its status ("Article cannot be modified in status PendingReview.") and
    /// when <c>version</c> is stale; a stale-version 409 carries <c>currentVersion</c>. Nothing is saved on a 400 or 409.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ArticleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArticleDto>> Update(
        Guid id, UpdateArticleRequest request, CancellationToken cancellationToken) =>
        Ok(await articles.UpdateAsync(
            currentUser.UserId,
            id,
            request.Title,
            request.Summary,
            request.Body,
            request.CategoryId,
            request.Version!.Value,
            cancellationToken));

    /// <summary>Deletes a Draft.</summary>
    /// <remarks>
    /// Only a Draft can be deleted: PendingReview, Published and Rejected articles answer 409. <c>version</c> is
    /// required; a stale one is a 409 with <c>currentVersion</c>.
    /// </remarks>
    /// <param name="id">The article.</param>
    /// <param name="version">The <c>version</c> of the article the author was looking at.</param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery, Required] uint? version, CancellationToken cancellationToken)
    {
        await articles.DeleteAsync(currentUser.UserId, id, version!.Value, cancellationToken);
        return NoContent();
    }
}
