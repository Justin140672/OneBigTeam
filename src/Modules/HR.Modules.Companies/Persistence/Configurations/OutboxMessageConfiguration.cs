using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(message => message.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(message => message.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(message => message.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(message => message.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(message => message.ProcessedAt)
            .HasColumnName("processed_at");

        builder.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(message => message.LastAttemptAt)
            .HasColumnName("last_attempt_at");

        builder.Property(message => message.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(500);

        builder.Property(message => message.FailedAt)
            .HasColumnName("failed_at");

        builder.HasIndex(message => message.CompanyId);

        builder.HasIndex(message => new { message.Status, message.CreatedAt });

        // SET-08: at most one in-flight (pending or processing) instruction per (company, event
        // type) at a time — enforced at the database level so concurrent numbering-format changes
        // can never race/run out of order. A second attempt to enqueue while one is already
        // in-flight fails this constraint; UpdateHrSettingsHandler checks for an in-flight row and
        // returns a Conflict before ever reaching SaveChangesAsync, but the index is the actual
        // safety net against a true race between two concurrent requests.
        builder.HasIndex(message => new { message.CompanyId, message.EventType })
            .HasFilter($"status IN ('{OutboxMessage.StatusPending}', '{OutboxMessage.StatusProcessing}')")
            .IsUnique()
            .HasDatabaseName("ix_outbox_messages_company_id_event_type_in_flight");
    }
}
