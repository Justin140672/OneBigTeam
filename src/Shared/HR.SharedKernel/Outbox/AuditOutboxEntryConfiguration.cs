using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.SharedKernel.Outbox;

/// <summary>
/// Column mapping for a module's own <see cref="IAuditOutboxEntry"/> entity - applied explicitly by
/// each module (e.g. <c>modelBuilder.ApplyConfiguration(new AuditOutboxEntryConfiguration&lt;AuditOutboxEntry&gt;())</c>)
/// since it lives outside the module's own assembly.
/// </summary>
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

        // The dispatcher scans for undelivered, due entries - an index starting with the highly
        // selective primary key would not help that query.
        builder.HasIndex(e => new { e.DispatchedAt, e.NextAttemptAt })
            .HasDatabaseName("ix_audit_outbox_pending");
    }
}
