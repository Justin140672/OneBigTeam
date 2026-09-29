using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.SharedKernel.Outbox;

public sealed class AuditOutboxEntryConfiguration<TEntry> : IEntityTypeConfiguration<TEntry>
    where TEntry : class, IAuditOutboxEntry
{
    public void Configure(EntityTypeBuilder<TEntry> builder)
    {
        builder.ToTable("audit_outbox");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20)
            .HasDefaultValue(OutboxChannel.Audit);
        builder.Property(e => e.EventTypeName).HasColumnName("event_type_name").HasMaxLength(500);
        builder.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.DispatchedAt).HasColumnName("dispatched_at");
        builder.Property(e => e.AttemptCount).HasColumnName("attempt_count");
        builder.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(e => e.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(e => e.IsTerminallyFailed).HasColumnName("is_terminally_failed");

        // Ticket 23 (P2): nullable so existing rows (written before this migration) remain
        // dispatchable without a backfill - see DbSetAuditOutboxExtensions.
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        builder.Property(e => e.CausationId).HasColumnName("causation_id");
        builder.Property(e => e.MessageId).HasColumnName("message_id");

        builder.HasIndex(e => new { e.DispatchedAt, e.NextAttemptAt })
            .HasDatabaseName("ix_audit_outbox_pending");
    }
}
