using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class ProcessedStripeEventConfiguration : IEntityTypeConfiguration<ProcessedStripeEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedStripeEvent> builder)
    {
        builder.ToTable("processed_stripe_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.StripeEventId)
            .HasColumnName("stripe_event_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EventCreatedAt)
            .HasColumnName("event_created_at")
            .IsRequired();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id");

        builder.Property(e => e.StripeSubscriptionId)
            .HasColumnName("stripe_subscription_id")
            .HasMaxLength(100);

        builder.Property(e => e.Applied)
            .HasColumnName("applied")
            .IsRequired();

        builder.Property(e => e.ProcessedAt)
            .HasColumnName("processed_at")
            .IsRequired();

        builder.HasIndex(e => e.StripeEventId)
            .IsUnique()
            .HasDatabaseName("ix_processed_stripe_events_stripe_event_id");

        builder.HasIndex(e => new { e.StripeSubscriptionId, e.EventCreatedAt })
            .HasDatabaseName("ix_processed_stripe_events_subscription_created");
    }
}
