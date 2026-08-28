using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence;

internal sealed class AuditPendingItemConfiguration : IEntityTypeConfiguration<AuditPendingItem>
{
    public void Configure(EntityTypeBuilder<AuditPendingItem> builder)
    {
        builder.ToTable("audit_pending_items");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        // AUD-01: one pending row per logical event — prevents double-enqueue.
        builder.Property(e => e.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.HasIndex(e => e.EventId)
            .IsUnique()
            .HasDatabaseName("ix_audit_pending_items_event_id");

        builder.Property(e => e.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2000);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.ProcessedAt)
            .HasColumnName("processed_at");

        // Promoter job queries by status — index covers the typical polling pattern.
        builder.HasIndex(e => e.Status)
            .HasFilter($"status IN ('{AuditPendingItem.StatusPending}', '{AuditPendingItem.StatusProcessing}')")
            .HasDatabaseName("ix_audit_pending_items_status_in_flight");
    }
}
