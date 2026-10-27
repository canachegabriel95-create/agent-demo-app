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

public sealed record ArticleTransitionRequest
{
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

    /// <summary>Lists the administrators' decisions on the article's submissions, newest first.</summary>
    /// <remarks>
    /// Each entry has the submission it belongs to, the decision, the reason (set when rejected) and when it was made.
    /// Who decided is never returned to authors. Empty until the first decision.
    /// </remarks>
    [HttpGet("{id:guid}/reviews")]
    [ProducesResponseType<IReadOnlyList<ArticleReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ArticleReviewDto>>> ListReviews(Guid id, CancellationToken cancellationToken) =>
        Ok(await articles.ListReviewsAsync(currentUser.UserId, id, cancellationToken));

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

    /// <summary>Sends a Draft for review, or resubmits a Rejected article.</summary>
    /// <remarks>
    /// Runs the full submission rules (title 5-150, summary 20-500, body 100-20000, a category): a 400 lists every
    /// broken field and changes nothing. On success the article is PendingReview, <c>submissionVersion</c> goes up by
    /// one and a snapshot of what was submitted is stored. 409 when the status does not allow it, when
    /// <c>version</c> is stale (with <c>currentVersion</c>), or when another request changed the article first.
    /// </remarks>
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<ArticleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArticleDto>> Submit(
        Guid id, ArticleTransitionRequest request, CancellationToken cancellationToken) =>
        Ok(await articles.SubmitAsync(currentUser.UserId, id, request.Version!.Value, cancellationToken));

    /// <summary>Takes an article back from review.</summary>
    /// <remarks>
    /// Only a PendingReview article can be withdrawn: it becomes a Draft again and its submission is marked withdrawn.
    /// If an administrator decides at the same moment exactly one of the two succeeds and the other gets 409.
    /// </remarks>
    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType<ArticleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArticleDto>> Withdraw(
        Guid id, ArticleTransitionRequest request, CancellationToken cancellationToken) =>
        Ok(await articles.WithdrawAsync(currentUser.UserId, id, request.Version!.Value, cancellationToken));
}
