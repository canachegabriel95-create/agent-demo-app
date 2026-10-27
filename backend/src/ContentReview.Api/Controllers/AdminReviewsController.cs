using System.ComponentModel.DataAnnotations;
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

/// <summary>What administrators review: the queue of pending submissions, one article in detail, and the author filter.</summary>
[Route("api/admin")]
public sealed class AdminReviewsController(IReviewQueueService reviews) : AdminControllerBase
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
}
