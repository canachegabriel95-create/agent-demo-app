using ContentReview.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(Category.NameMaxLength).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(Category.SlugMaxLength).IsRequired();

        builder.HasIndex(c => c.Name).IsUnique();
        builder.HasIndex(c => c.Slug).IsUnique();

        // The list is fixed in this version. Fixed ids keep every database (and every test run) identical.
        builder.HasData(CategorySeed.All.Select(c => new
        {
            c.Id,
            c.Name,
            c.Slug,
            c.SortOrder,
        }));
    }
}
