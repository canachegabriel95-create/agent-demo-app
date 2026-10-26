using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Content Review database. Entity sets and configurations
/// (<c>Persistence/Configurations/*Configuration.cs</c>) are added by F01.4.
/// </summary>
public class ContentReviewDbContext(DbContextOptions<ContentReviewDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContentReviewDbContext).Assembly);
    }
}
