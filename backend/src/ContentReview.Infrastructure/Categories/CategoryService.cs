using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Categories;

public sealed class CategoryService(ContentReviewDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryItem(c.Id, c.Name, c.Slug))
            .ToListAsync(cancellationToken);
}
