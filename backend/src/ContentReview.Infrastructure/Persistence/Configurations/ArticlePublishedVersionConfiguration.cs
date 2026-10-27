using ContentReview.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class ArticlePublishedVersionConfiguration : IEntityTypeConfiguration<ArticlePublishedVersion>
{
    public void Configure(EntityTypeBuilder<ArticlePublishedVersion> builder)
    {
        builder.ToTable("article_published_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Title).HasMaxLength(Article.TitleMaxLength).IsRequired();
        builder.Property(v => v.Summary).HasMaxLength(Article.SummaryMaxLength).IsRequired();
        builder.Property(v => v.Body).HasMaxLength(Article.BodyMaxLength).IsRequired();

        builder.HasIndex(v => new { v.ArticleId, v.Version }).IsUnique();

        builder.HasOne<Article>().WithMany().HasForeignKey(v => v.ArticleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(v => v.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
