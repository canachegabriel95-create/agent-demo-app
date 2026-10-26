using ContentReview.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Action).HasMaxLength(AuditEvent.NameMaxLength).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(AuditEvent.NameMaxLength).IsRequired();
        builder.Property(e => e.Details).HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(AuditEvent.NameMaxLength);

        builder.HasIndex(e => e.OccurredAt).IsDescending();
        builder.HasIndex(e => e.Action);
        builder.HasIndex(e => e.ActorUserId);

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
