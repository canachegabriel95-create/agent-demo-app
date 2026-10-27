using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Domain.Common;
using ContentReview.Infrastructure.Reviews;
using ContentReview.Infrastructure.Users;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ContentReview.Domain.Exceptions.ValidationException;

namespace ContentReview.Api.Controllers;

public sealed class ReviewQueueQuery
{
    /// <summary>Only articles of this author.</summary>
    public Guid? AuthorId { get; init; }

    /// <summary>Only articles in this category (one of <c>GET /api/categories</c>).</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>The only sort key today: <c>submittedAt</c>.</summary>
    public ReviewQueueSort Sort { get; init; } = ReviewQueueSort.SubmittedAt;

    /// <summary><c>asc</c> (default, oldest submission first) or <c>desc</c>.</summary>
    public SortDirection Direction { get; init; } = SortDirection.Asc;

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, PagedResult<ReviewQueueItem>.MaxPageSize)]
    public int PageSize { get; init; } = PagedResult<ReviewQueueItem>.DefaultPageSize;
}

public sealed record ApproveRequest
{
    /// <summary>The <c>version</c> of the article the administrator was looking at.</summary>
    [Required]
    public uint? Version { get; init; }

    /// <summary>The <c>submissionVersion</c> that was opened: a newer submission is a different thing to decide on.</summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int? SubmissionVersion { get; init; }
}

public sealed record RejectRequest
{
    [Required]
    public uint? Version { get; init; }

    [Required]
    [Range(1, int.MaxValue)]
    public int? SubmissionVersion { get; init; }

    /// <summary>Why the article is sent back, 10-1000 characters after trimming. The author sees it.</summary>
    public string? Reason { get; init; }
}

/// <summary>What administrators review: the queue of pending submissions, one article in detail, and the author filter.</summary>
[Route("api/admin")]
public sealed class AdminReviewsController(
    IReviewQueueService reviews, IReviewDecisionService decisions, ICurrentUser currentUser) : AdminControllerBase
{
    /// <summary>Lists the articles waiting for review.</summary>
    /// <remarks>
    /// Only PendingReview articles, oldest submission first by default. Filter with <c>authorId</c> and
    /// <c>categoryId</c>; sort with <c>sort=submittedAt</c> and <c>direction=asc|desc</c>; page with <c>page</c> and
    /// <c>pageSize</c> (up to 100). Articles of suspended authors are included: each item carries the author's status.
    /// </remarks>
    [HttpGet("reviews/queue")]
    [ProducesResponseType<PagedResult<ReviewQueueItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ReviewQueueItem>>> Queue(
        [FromQuery] ReviewQueueQuery query, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(query.Sort) || !Enum.IsDefined(query.Direction))
        {
            throw new ValidationException("sort", "Sort by submittedAt, direction asc or desc.");
        }

        return Ok(await reviews.ListQueueAsync(
            new ReviewQueueFilter(query.AuthorId, query.CategoryId, query.Sort, query.Direction, query.Page, query.PageSize),
            cancellationToken));
    }

    /// <summary>Returns a submitted article for review, read-only, in any status.</summary>
    /// <remarks>
    /// Title, summary, body and category are those of the current submission (the snapshot), not of the author's draft.
    /// Includes the author with account status and the full review history with reviewers. <c>canReview</c> is true only
    /// while the article is PendingReview and its author is Active; otherwise <c>reviewBlockedReason</c> says why.
    /// An article that was never submitted is a private draft: 404.
    /// </remarks>
    [HttpGet("articles/{id:guid}")]
    [ProducesResponseType<ReviewArticleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewArticleDetails>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await reviews.GetAsync(id, cancellationToken));

    /// <summary>Finds authors for the queue's author filter.</summary>
    /// <remarks>Up to 20 authors whose name or email contains <c>search</c> (ignoring case), by name. Empty search: the first 20.</remarks>
    [HttpGet("authors/lookup")]
    [ProducesResponseType<IReadOnlyList<AuthorLookupItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AuthorLookupItem>>> LookupAuthors(
        [FromQuery, MaxLength(100)] string? search, CancellationToken cancellationToken) =>
        Ok(await reviews.LookupAuthorsAsync(search, cancellationToken));

    /// <summary>Publishes a pending article.</summary>
    /// <remarks>
    /// Sets Published and <c>publishedAt</c>, stores the review (reviewer, decision, time, submission version) and writes
    /// an <c>ArticleApproved</c> audit event in one transaction. Checked in order: 404 unknown article; 409 author
    /// suspended; 409 not pending, a newer submission, or a stale <c>version</c>; 409 when another request decided or
    /// the author withdrew first. Every 409 carries <c>currentStatus</c> and <c>currentVersion</c>. A submission can be
    /// decided once. There is no endpoint that edits an article's content.
    /// </remarks>
    [HttpPost("articles/{id:guid}/approve")]
    [ProducesResponseType<ReviewArticleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewArticleDetails>> Approve(
        Guid id, ApproveRequest request, CancellationToken cancellationToken) =>
        Ok(await decisions.ApproveAsync(
            id, currentUser.UserId, request.Version!.Value, request.SubmissionVersion!.Value, cancellationToken));

    /// <summary>Sends a pending article back to its author with a reason.</summary>
    /// <remarks>
    /// Same checks and conflicts as approve, then the reason (10-1000 characters, 400 otherwise). The article becomes
    /// Rejected, the reason is stored on the review and shown to the author, and an <c>ArticleRejected</c> audit event is
    /// written.
    /// </remarks>
    [HttpPost("articles/{id:guid}/reject")]
    [ProducesResponseType<ReviewArticleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewArticleDetails>> Reject(
        Guid id, RejectRequest request, CancellationToken cancellationToken) =>
        Ok(await decisions.RejectAsync(
            id, currentUser.UserId, request.Version!.Value, request.SubmissionVersion!.Value, request.Reason, cancellationToken));
}
