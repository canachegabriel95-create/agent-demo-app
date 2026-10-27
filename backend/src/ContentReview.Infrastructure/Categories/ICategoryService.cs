namespace ContentReview.Infrastructure.Categories;

public sealed record CategoryItem(Guid Id, string Name, string Slug);

/// <summary>The fixed list of article categories. There is no way to change it through the application.</summary>
public interface ICategoryService
{
    /// <summary>All categories in display order (<c>sort_order</c>).</summary>
    Task<IReadOnlyList<CategoryItem>> ListAsync(CancellationToken cancellationToken = default);
}
