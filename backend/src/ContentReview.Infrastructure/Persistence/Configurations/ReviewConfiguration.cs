using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", t =>
            t.HasCheckConstraint("ck_reviews_decision", EnumColumn.CheckSql<ReviewDecision>("decision")));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Decision).AsText();
        builder.Property(r => r.Reason).HasMaxLength(Review.ReasonMaxLength);

        // A submission can be reviewed only once; this is what makes racing reviews safe.
        builder.HasIndex(r => new { r.ArticleId, r.SubmissionVersion }).IsUnique();

        builder.HasOne<Article>().WithMany().HasForeignKey(r => r.ArticleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReviewerId).OnDelete(DeleteBehavior.Restrict);
    }
}
