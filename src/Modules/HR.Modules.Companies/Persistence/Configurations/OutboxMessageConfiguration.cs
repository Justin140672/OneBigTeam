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

        builder.HasIndex(message => message.CompanyId);

        builder.HasIndex(message => new { message.Status, message.CreatedAt });
    }
}
