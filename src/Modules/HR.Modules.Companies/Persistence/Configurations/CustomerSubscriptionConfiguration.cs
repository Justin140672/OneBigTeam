using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class CustomerSubscriptionConfiguration : IEntityTypeConfiguration<CustomerSubscription>
{
    public void Configure(EntityTypeBuilder<CustomerSubscription> builder)
    {
        builder.ToTable("customer_subscriptions");

        builder.HasKey(s => s.CompanyId);

        builder.Property(s => s.CompanyId)
            .HasColumnName("company_id")
            .ValueGeneratedNever();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.TrialStartedAt)
            .HasColumnName("trial_started_at")
            .IsRequired();

        builder.Property(s => s.TrialExpiresAt)
            .HasColumnName("trial_expires_at")
            .IsRequired();

        builder.Property(s => s.StripeCustomerId)
            .HasColumnName("stripe_customer_id")
            .HasMaxLength(100);

        builder.Property(s => s.StripeSubscriptionId)
            .HasColumnName("stripe_subscription_id")
            .HasMaxLength(100);

        builder.Property(s => s.PriceId)
            .HasColumnName("price_id")
            .HasMaxLength(100);

        builder.Property(s => s.CurrentPeriodEnd)
            .HasColumnName("current_period_end");

        builder.Property(s => s.CancelAtPeriodEnd)
            .HasColumnName("cancel_at_period_end")
            .IsRequired();

        builder.Property(s => s.AdminForcedReadOnly)
            .HasColumnName("admin_forced_read_only")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.DeletionScheduledAt)
            .HasColumnName("deletion_scheduled_at");

        builder.Property(s => s.DeletionScheduledBy)
            .HasColumnName("deletion_scheduled_by");

        builder.Property(s => s.DeletionCancelledAt)
            .HasColumnName("deletion_cancelled_at");

        builder.Property(s => s.DeletionExecutedAt)
            .HasColumnName("deletion_executed_at");

        builder.Property(s => s.LegalHoldPlacedAt)
            .HasColumnName("legal_hold_placed_at");

        builder.Property(s => s.LegalHoldPlacedBy)
            .HasColumnName("legal_hold_placed_by");

        builder.Property(s => s.LegalHoldReason)
            .HasColumnName("legal_hold_reason")
            .HasMaxLength(1000);

        builder.Ignore(s => s.HasPendingDeletion);
        builder.Ignore(s => s.IsUnderLegalHold);

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<Company>()
            .WithOne()
            .HasForeignKey<CustomerSubscription>(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
