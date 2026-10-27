using ContentReview.Api.Auth;
using ContentReview.Infrastructure.Reviews;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

/// <summary>
/// What administrators review when an author edited a published article: the revision next to the published version,
/// and the decision. Revisions of the same queue as new submissions: see <c>GET /api/admin/reviews/queue</c>.
/// </summary>
[Route("api/admin/revisions")]
public sealed class AdminRevisionsController(
    IReviewQueueService reviews, IRevisionDecisionService decisions, ICurrentUser currentUser) : AdminControllerBase
{
    /// <summary>Returns a submitted revision for review, read-only, in any status, next to the published version.</summary>
    /// <remarks>
    /// Title, summary, body and category are those of the revision; <c>published</c> is what readers see now. Includes
    /// the author with account status and the full review history with reviewers. <c>canReview</c> is true only while
    /// the revision is PendingReview and its author is Active; otherwise <c>reviewBlockedReason</c> says why. A
    /// revision that was never submitted is the author's private draft: 404.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReviewRevisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewRevisionDetails>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await reviews.GetRevisionAsync(id, cancellationToken));

    /// <summary>Approves a pending revision: its content replaces the published content.</summary>
    /// <remarks>
    /// In one transaction the revision becomes Approved, the article gets its content, <c>publishedAt</c> and the next
    /// <c>publishedVersion</c>, the replaced version is stored in the history and the review is recorded. The article stays
    /// Published throughout. Checked in order: 404 unknown revision; 409 author suspended; 409 not pending, a newer
    /// submission, or a stale <c>version</c>; 409 when another request decided or the author withdrew first. Every 409
    /// carries <c>currentStatus</c> and <c>currentVersion</c>. A submission can be decided once.
    /// </remarks>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<ReviewRevisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewRevisionDetails>> Approve(
        Guid id, ApproveRequest request, CancellationToken cancellationToken) =>
        Ok(await decisions.ApproveAsync(
            id, currentUser.UserId, request.Version!.Value, request.SubmissionVersion!.Value, cancellationToken));

    /// <summary>Sends a pending revision back to its author with a reason.</summary>
    /// <remarks>
    /// Same checks and conflicts as approve, then the reason (10-1000 characters, 400 otherwise). The revision becomes
    /// Rejected and the reason is shown to the author. The published article is not changed.
    /// </remarks>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<ReviewRevisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewRevisionDetails>> Reject(
        Guid id, RejectRequest request, CancellationToken cancellationToken) =>
        Ok(await decisions.RejectAsync(
            id, currentUser.UserId, request.Version!.Value, request.SubmissionVersion!.Value, request.Reason, cancellationToken));
}
