using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.ToTable("articles", t =>
            t.HasCheckConstraint("ck_articles_status", EnumColumn.CheckSql<ArticleStatus>("status")));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Title).HasMaxLength(Article.TitleMaxLength).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(Article.SummaryMaxLength).IsRequired();
        builder.Property(a => a.Body).HasMaxLength(Article.BodyMaxLength).IsRequired();
        builder.Property(a => a.Status).AsText();
        builder.Property(a => a.SubmissionVersion).HasDefaultValue(0);
        builder.Property(a => a.PublishedVersion).HasDefaultValue(0);

        builder.Property(a => a.Version).IsRowVersion();

        builder.HasIndex(a => new { a.AuthorId, a.Status });
        builder.HasIndex(a => new { a.Status, a.SubmittedAt });

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
