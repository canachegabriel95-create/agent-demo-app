using ContentReview.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Code).HasMaxLength(Invitation.CodeMaxLength).IsRequired();
        builder.Property(i => i.Email).HasMaxLength(254).IsRequired();
        builder.Property(i => i.NormalizedEmail).HasMaxLength(254).IsRequired();

        builder.Property(i => i.Version).IsRowVersion();

        builder.HasIndex(i => i.Code).IsUnique();
        builder.HasIndex(i => i.NormalizedEmail);

        // An account can consume at most one invitation.
        builder.HasIndex(i => i.UsedByUserId).IsUnique().HasFilter("used_by_user_id IS NOT NULL");

        builder.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.UsedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
