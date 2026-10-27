using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Domain.Common;
using ContentReview.Infrastructure.Articles;
using Microsoft.AspNetCore.Mvc;

namespace ContentReview.Api.Controllers;

public sealed class FeedQuery
{
    /// <summary>Any part of the title, ignoring case. <c>%</c> and <c>_</c> are ordinary characters.</summary>
    [MaxLength(150)]
    public string? Search { get; init; }

    public Guid? CategoryId { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, PagedResult<FeedItem>.MaxPageSize)]
    public int PageSize { get; init; } = PagedResult<FeedItem>.DefaultPageSize;
}

/// <summary>The published articles of all authors. Read-only: there is nothing to write here.</summary>
[Route("api/feed")]
public sealed class FeedController(IFeedService feed) : AuthorControllerBase
{
    /// <summary>Lists the published articles, newest publication first.</summary>
    /// <remarks>
    /// Only Published articles are ever returned, whoever wrote them and whether or not the author is still active.
    /// Filter with <c>search</c> (title) and <c>categoryId</c>; paged with <c>page</c> and <c>pageSize</c> (up to 100).
    /// Authors are named by display name only.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<FeedItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<FeedItem>>> List(
        [FromQuery] FeedQuery query, CancellationToken cancellationToken) =>
        Ok(await feed.ListAsync(new FeedFilter(query.Search, query.CategoryId, query.Page, query.PageSize), cancellationToken));

    /// <summary>Reads one published article in full.</summary>
    /// <remarks>
    /// The text is the submission the administrator approved. A draft, pending, rejected or unknown id is a 404: the
    /// response never reveals that such an article exists.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<FeedArticle>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FeedArticle>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await feed.GetAsync(id, cancellationToken));
}
