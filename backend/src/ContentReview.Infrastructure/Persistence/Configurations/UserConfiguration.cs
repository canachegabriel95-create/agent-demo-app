using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t =>
        {
            t.HasCheckConstraint("ck_users_role", EnumColumn.CheckSql<UserRole>("role"));
            t.HasCheckConstraint("ck_users_status", EnumColumn.CheckSql<UserStatus>("status"));
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(User.PasswordHashMaxLength).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(User.FullNameMaxLength).IsRequired();
        builder.Property(u => u.Bio).HasMaxLength(User.BioMaxLength);
        builder.Property(u => u.Role).AsText();
        builder.Property(u => u.Status).AsText();
        builder.Property(u => u.SuspensionReason).HasMaxLength(500);

        // PostgreSQL xmin system column as the optimistic concurrency token.
        builder.Property(u => u.Version).IsRowVersion();

        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
        builder.HasIndex(u => new { u.Role, u.Status });
    }
}
