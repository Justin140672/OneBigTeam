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
