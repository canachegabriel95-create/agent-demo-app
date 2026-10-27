using ContentReview.Api.Auth;
using ContentReview.Infrastructure.Revisions;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

/// <summary>
/// An author's revision of one of their published articles. The article has at most one open revision, so the article
/// id is enough. Editing a revision never changes what readers see; only an administrator's approval does. Somebody
/// else's article is a 404, exactly like one that does not exist.
/// </summary>
[Route("api/articles/{articleId:guid}/revision")]
public sealed class ArticleRevisionsController(IRevisionService revisions, ICurrentUser currentUser) : AuthorControllerBase
{
    /// <summary>Starts a revision of a published article.</summary>
    /// <remarks>
    /// The revision begins as a copy of the published content and is a Draft. 409 when the article is not Published or
    /// already has a revision in progress; suspended authors are refused. The published article is not touched.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Create(Guid articleId, CancellationToken cancellationToken)
    {
        var created = await revisions.CreateAsync(currentUser.UserId, articleId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { articleId }, created);
    }

    /// <summary>Returns the revision in progress with the actions the lifecycle allows now.</summary>
    /// <remarks>404 when the article is not the caller's or has no open revision.</remarks>
    [HttpGet]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RevisionDto>> Get(Guid articleId, CancellationToken cancellationToken) =>
        Ok(await revisions.GetAsync(currentUser.UserId, articleId, cancellationToken));

    /// <summary>Lists the administrators' decisions on the article's revisions, newest first.</summary>
    /// <remarks>
    /// Each entry has the revision number, the submission it belongs to, the decision, the reason (set when rejected)
    /// and when it was made. Earlier decisions stay after the revision was edited and resubmitted or approved. Who
    /// decided is never returned to authors.
    /// </remarks>
    [HttpGet("reviews")]
    [ProducesResponseType<IReadOnlyList<RevisionReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RevisionReviewDto>>> ListReviews(Guid articleId, CancellationToken cancellationToken) =>
        Ok(await revisions.ListReviewsAsync(currentUser.UserId, articleId, cancellationToken));

    /// <summary>Replaces the content of a Draft or Rejected revision.</summary>
    /// <remarks>
    /// The same draft limits as for an article (title 1-150, summary at most 500, body at most 20000). 409 when the
    /// revision cannot be edited in its status and when <c>version</c> is stale (with <c>currentVersion</c>). Nothing is
    /// saved on a 400 or 409.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Update(
        Guid articleId, UpdateArticleRequest request, CancellationToken cancellationToken) =>
        Ok(await revisions.UpdateAsync(
            currentUser.UserId,
            articleId,
            request.Title,
            request.Summary,
            request.Body,
            request.CategoryId,
            request.Version!.Value,
            cancellationToken));

    /// <summary>Sends the revision for review, or resubmits a rejected one.</summary>
    /// <remarks>
    /// Runs the full submission rules (title 5-150, summary 20-500, body 100-20000, a category): a 400 lists every
    /// broken field and changes nothing. On success the revision is PendingReview and <c>submissionVersion</c> goes up
    /// by one. 409 when the status does not allow it, when <c>version</c> is stale, or when another request changed the
    /// revision first.
    /// </remarks>
    [HttpPost("submit")]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Submit(
        Guid articleId, ArticleTransitionRequest request, CancellationToken cancellationToken) =>
        Ok(await revisions.SubmitAsync(currentUser.UserId, articleId, request.Version!.Value, cancellationToken));

    /// <summary>Takes the revision back from review.</summary>
    /// <remarks>
    /// Only a PendingReview revision can be withdrawn: it becomes a Draft again. If an administrator decides at the same
    /// moment exactly one of the two succeeds and the other gets 409.
    /// </remarks>
    [HttpPost("withdraw")]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Withdraw(
        Guid articleId, ArticleTransitionRequest request, CancellationToken cancellationToken) =>
        Ok(await revisions.WithdrawAsync(currentUser.UserId, articleId, request.Version!.Value, cancellationToken));
}
