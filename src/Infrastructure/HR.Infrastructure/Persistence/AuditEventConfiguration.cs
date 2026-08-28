using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        // AUD-01: stable idempotency key — unique so duplicate promotions are rejected safely.
        builder.Property(e => e.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.HasIndex(e => e.EventId)
            .IsUnique()
            .HasDatabaseName("ix_audit_events_event_id");

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.EntityId)
            .HasColumnName("entity_id")
            .IsRequired();

        builder.Property(e => e.EmployeeId)
            .HasColumnName("employee_id");

        builder.Property(e => e.ActorUserId)
            .HasColumnName("actor_user_id");

        builder.Property(e => e.ActorEmployeeId)
            .HasColumnName("actor_employee_id");

        // AUD-04: actor origin classification stored as integer enum.
        builder.Property(e => e.ActorType)
            .HasColumnName("actor_type")
            .IsRequired();

        builder.Property(e => e.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasColumnName("correlation_id");

        builder.Property(e => e.Summary)
            .HasColumnName("summary")
            .HasMaxLength(500);

        builder.Property(e => e.BeforeJson)
            .HasColumnName("before_json")
            .HasColumnType("jsonb");

        builder.Property(e => e.AfterJson)
            .HasColumnName("after_json")
            .HasColumnType("jsonb");

        builder.Property(e => e.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => new { e.CompanyId, e.OccurredAt });
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => new { e.CompanyId, e.EmployeeId, e.OccurredAt });
    }
}
