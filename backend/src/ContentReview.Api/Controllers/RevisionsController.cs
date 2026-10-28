using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Infrastructure.Revisions;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

public sealed record UpdateRevisionRequest
{
    /// <summary>Required, 1-150 characters after trimming.</summary>
    public string? Title { get; init; }

    public string? Summary { get; init; }

    public string? Body { get; init; }

    /// <summary><c>null</c> clears the category (a revision cannot be submitted without one).</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>The <c>version</c> of the revision the author was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

public sealed record RevisionTransitionRequest
{
    /// <summary>The <c>version</c> of the revision the author was looking at.</summary>
    [Required]
    public uint? Version { get; init; }
}

/// <summary>
/// Revisions of an author's own published articles. A revision is a separate version that goes through review while the
/// published version stays visible. Somebody else's article or revision is a 404, exactly like one that does not exist.
/// </summary>
public sealed class RevisionsController(IRevisionService revisions, ICurrentUser currentUser) : AuthorControllerBase
{
    /// <summary>Starts a revision of a published article.</summary>
    /// <remarks>
    /// The revision is a Draft copy of the published content; the published version is not changed. 409 when the article
    /// is not Published ("Article cannot be revised in status Draft.") or already has an open revision (only one
    /// revision per article can be in progress).
    /// </remarks>
    [HttpPost("api/articles/{articleId:guid}/revisions")]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Create(Guid articleId, CancellationToken cancellationToken)
    {
        var created = await revisions.CreateAsync(currentUser.UserId, articleId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Lists the revisions of one of the caller's articles, newest first.</summary>
    /// <remarks>Includes the approved ones; each entry has its decisions (with the reason of a rejection).</remarks>
    [HttpGet("api/articles/{articleId:guid}/revisions")]
    [ProducesResponseType<IReadOnlyList<RevisionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RevisionDto>>> List(Guid articleId, CancellationToken cancellationToken) =>
        Ok(await revisions.ListAsync(currentUser.UserId, articleId, cancellationToken));

    /// <summary>Returns one of the caller's revisions with the actions the lifecycle allows now.</summary>
    [HttpGet("api/revisions/{id:guid}")]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RevisionDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await revisions.GetAsync(currentUser.UserId, id, cancellationToken));

    /// <summary>Replaces the content of a Draft or Rejected revision.</summary>
    /// <remarks>
    /// Same draft limits as an article. 409 when the revision cannot be edited in its status and when <c>version</c> is
    /// stale (the latter carries <c>currentVersion</c>). The published article is never changed by an edit.
    /// </remarks>
    [HttpPut("api/revisions/{id:guid}")]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Update(Guid id, UpdateRevisionRequest request, CancellationToken cancellationToken) =>
        Ok(await revisions.UpdateAsync(
            currentUser.UserId,
            id,
            request.Title,
            request.Summary,
            request.Body,
            request.CategoryId,
            request.Version!.Value,
            cancellationToken));

    /// <summary>Discards a Draft or Rejected revision.</summary>
    /// <remarks>
    /// The published article stays as it is and a new revision can be started. A revision that is PendingReview (withdraw
    /// it first) or Approved answers 409. <c>version</c> is required; a stale one is a 409 with <c>currentVersion</c>.
    /// </remarks>
    /// <param name="id">The revision.</param>
    /// <param name="version">The <c>version</c> of the revision the author was looking at.</param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("api/revisions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery, Required] uint? version, CancellationToken cancellationToken)
    {
        await revisions.DeleteAsync(currentUser.UserId, id, version!.Value, cancellationToken);
        return NoContent();
    }

    /// <summary>Sends a Draft revision for review, or resubmits a Rejected one.</summary>
    /// <remarks>
    /// Runs the same submission rules as an article (title 5-150, summary 20-500, body 100-20000, a category): a 400
    /// lists every broken field and changes nothing. On success the revision is PendingReview and read-only.
    /// </remarks>
    [HttpPost("api/revisions/{id:guid}/submit")]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Submit(Guid id, RevisionTransitionRequest request, CancellationToken cancellationToken) =>
        Ok(await revisions.SubmitAsync(currentUser.UserId, id, request.Version!.Value, cancellationToken));

    /// <summary>Takes a revision back from review; it becomes an editable Draft again.</summary>
    /// <remarks>
    /// If an administrator decides at the same moment exactly one of the two succeeds and the other gets 409.
    /// </remarks>
    [HttpPost("api/revisions/{id:guid}/withdraw")]
    [ProducesResponseType<RevisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevisionDto>> Withdraw(Guid id, RevisionTransitionRequest request, CancellationToken cancellationToken) =>
        Ok(await revisions.WithdrawAsync(currentUser.UserId, id, request.Version!.Value, cancellationToken));
}
