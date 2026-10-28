using ContentReview.Api.Auth;
using ContentReview.Infrastructure.Reviews;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

/// <summary>
/// What administrators review besides first submissions: revisions of published articles. A revision is read next to
/// the version it would replace and decided on like a submission; the queue (<c>GET /api/admin/reviews/queue</c>) lists
/// both kinds.
/// </summary>
[Route("api/admin/revisions")]
public sealed class AdminRevisionsController(IRevisionReviewService reviews, ICurrentUser currentUser) : AdminControllerBase
{
    /// <summary>Returns a submitted revision for review, read-only, in any status.</summary>
    /// <remarks>
    /// Title, summary, body and category are the revision's; <c>published</c> is the version readers see now. Includes the
    /// author with account status and the decisions on earlier submissions of this revision. <c>canReview</c> is true only
    /// while the revision is PendingReview and its author is Active; otherwise <c>reviewBlockedReason</c> says why. A
    /// revision that was never submitted is a private draft: 404.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReviewRevisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewRevisionDetails>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await reviews.GetAsync(id, cancellationToken));

    /// <summary>Approves a pending revision: its content replaces the published content.</summary>
    /// <remarks>
    /// The swap, the review and the history of the replaced version are one transaction; readers see either the old or
    /// the new version, never a mix. Checked in order: 404 unknown revision; 409 author suspended; 409 not pending, a
    /// newer submission, or a stale <c>version</c>; 409 when another request decided or the author withdrew first. Every
    /// 409 carries <c>currentStatus</c> and <c>currentVersion</c>.
    /// </remarks>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<ReviewRevisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewRevisionDetails>> Approve(Guid id, ApproveRequest request, CancellationToken cancellationToken) =>
        Ok(await reviews.ApproveAsync(
            id, currentUser.UserId, request.Version!.Value, request.SubmissionVersion!.Value, cancellationToken));

    /// <summary>Sends a pending revision back to its author with a reason.</summary>
    /// <remarks>
    /// Same checks and conflicts as approve, then the reason (10-1000 characters, 400 otherwise). The revision becomes
    /// Rejected and the author sees the reason; the published version is not touched.
    /// </remarks>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<ReviewRevisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewRevisionDetails>> Reject(Guid id, RejectRequest request, CancellationToken cancellationToken) =>
        Ok(await reviews.RejectAsync(
            id, currentUser.UserId, request.Version!.Value, request.SubmissionVersion!.Value, request.Reason, cancellationToken));
}
