namespace ContentReview.Domain.Common;

/// <summary>A page of a list endpoint: <c>{ items, page, pageSize, totalCount }</c>.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}
