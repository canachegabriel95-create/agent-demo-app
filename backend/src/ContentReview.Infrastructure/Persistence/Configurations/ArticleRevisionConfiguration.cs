using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class ArticleRevisionConfiguration : IEntityTypeConfiguration<ArticleRevision>
{
    public void Configure(EntityTypeBuilder<ArticleRevision> builder)
    {
        builder.ToTable("article_revisions", t =>
            t.HasCheckConstraint("ck_article_revisions_status", EnumColumn.CheckSql<RevisionStatus>("status")));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Title).HasMaxLength(Article.TitleMaxLength).IsRequired();
        builder.Property(r => r.Summary).HasMaxLength(Article.SummaryMaxLength).IsRequired();
        builder.Property(r => r.Body).HasMaxLength(Article.BodyMaxLength).IsRequired();
        builder.Property(r => r.Status).AsText();
        builder.Property(r => r.SubmissionVersion).HasDefaultValue(0);
        builder.Ignore(r => r.IsOpen);

        builder.Property(r => r.Version).IsRowVersion();

        // At most one open revision per article: this index is what keeps two revisions from ever being live together.
        builder.HasIndex(r => r.ArticleId)
            .IsUnique()
            .HasDatabaseName("ux_article_revisions_open_per_article")
            .HasFilter("status <> 'Approved'");
        builder.HasIndex(r => new { r.Status, r.SubmittedAt });

        builder.HasOne<Article>().WithMany().HasForeignKey(r => r.ArticleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
