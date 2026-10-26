using ContentReview.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class ArticleSubmissionConfiguration : IEntityTypeConfiguration<ArticleSubmission>
{
    public void Configure(EntityTypeBuilder<ArticleSubmission> builder)
    {
        builder.ToTable("article_submissions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Title).HasMaxLength(Article.TitleMaxLength).IsRequired();
        builder.Property(s => s.Summary).HasMaxLength(Article.SummaryMaxLength).IsRequired();
        builder.Property(s => s.Body).HasMaxLength(Article.BodyMaxLength).IsRequired();

        builder.HasIndex(s => new { s.ArticleId, s.Version }).IsUnique();

        builder.HasOne<Article>().WithMany().HasForeignKey(s => s.ArticleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(s => s.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
