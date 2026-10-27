using System.ComponentModel.DataAnnotations;
using ContentReview.Api.Auth;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Users;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ContentReview.Domain.Exceptions.ValidationException;

namespace ContentReview.Api.Controllers;

public sealed class MyArticlesQuery
{
    /// <summary>Any part of the title, ignoring case. <c>%</c> and <c>_</c> are ordinary characters.</summary>
    [MaxLength(150)]
    public string? Search { get; init; }

    /// <summary>Draft, PendingReview, Published or Rejected.</summary>
    public ArticleStatus? Status { get; init; }

    public Guid? CategoryId { get; init; }

    /// <summary><c>updatedAt</c> (default), <c>submittedAt</c> or <c>title</c>.</summary>
    public MyArticleSort Sort { get; init; } = MyArticleSort.UpdatedAt;

    /// <summary>Defaults to <c>desc</c> for the dates (newest first) and <c>asc</c> for the title.</summary>
    public SortDirection? Direction { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, PagedResult<MyArticleItem>.MaxPageSize)]
    public int PageSize { get; init; } = PagedResult<MyArticleItem>.DefaultPageSize;
}

/// <summary>The signed-in author's overview of their own articles (including their published ones).</summary>
[Route("api/me/articles")]
public sealed class MyArticlesController(IMyArticlesService articles, ICurrentUser currentUser) : AuthorControllerBase
{
    /// <summary>Lists the caller's own articles.</summary>
    /// <remarks>
    /// Paged (<c>page</c>, <c>pageSize</c> up to 100). Filter with <c>search</c> (title), <c>status</c> and
    /// <c>categoryId</c>; sort with <c>sort</c> (updatedAt, submittedAt, title) and <c>direction</c> (asc, desc).
    /// Drafts have no submission date and come last when sorting by it. Other authors' articles are never returned.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<MyArticleItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<MyArticleItem>>> List(
        [FromQuery] MyArticlesQuery query, CancellationToken cancellationToken)
    {
        if (query.Status is { } status && !Enum.IsDefined(status))
        {
            throw new ValidationException("status", "Status must be Draft, PendingReview, Published or Rejected.");
        }

        if (!Enum.IsDefined(query.Sort) || query.Direction is { } direction && !Enum.IsDefined(direction))
        {
            throw new ValidationException("sort", "Sort by updatedAt, submittedAt or title, direction asc or desc.");
        }

        var effective = query.Direction ?? (query.Sort == MyArticleSort.Title ? SortDirection.Asc : SortDirection.Desc);
        return Ok(await articles.ListAsync(
            currentUser.UserId,
            new MyArticlesFilter(query.Search, query.Status, query.CategoryId, query.Sort, effective, query.Page, query.PageSize),
            cancellationToken));
    }

    /// <summary>Counts the caller's articles per status.</summary>
    /// <remarks>Always all four statuses; one without articles is 0.</remarks>
    [HttpGet("stats")]
    [ProducesResponseType<ArticleStatusCounts>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ArticleStatusCounts>> Stats(CancellationToken cancellationToken) =>
        Ok(await articles.StatsAsync(currentUser.UserId, cancellationToken));
}
