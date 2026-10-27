using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class RevisionReviewConfiguration : IEntityTypeConfiguration<RevisionReview>
{
    public void Configure(EntityTypeBuilder<RevisionReview> builder)
    {
        builder.ToTable("revision_reviews", t =>
            t.HasCheckConstraint("ck_revision_reviews_decision", EnumColumn.CheckSql<ReviewDecision>("decision")));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Decision).AsText();
        builder.Property(r => r.Reason).HasMaxLength(Review.ReasonMaxLength);

        // A submission of a revision can be reviewed only once, however many administrators act at the same moment.
        builder.HasIndex(r => new { r.RevisionId, r.SubmissionVersion }).IsUnique();

        builder.HasOne<ArticleRevision>().WithMany().HasForeignKey(r => r.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReviewerId).OnDelete(DeleteBehavior.Restrict);
    }
}
