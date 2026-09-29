using HR.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Notifications.Persistence.Configurations;

internal sealed class OperationalAlertEmailDeliveryConfiguration : IEntityTypeConfiguration<OperationalAlertEmailDelivery>
{
    public void Configure(EntityTypeBuilder<OperationalAlertEmailDelivery> builder)
    {
        builder.ToTable("operational_alert_email_deliveries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.AlertId)
            .HasColumnName("alert_id")
            .IsRequired();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasDefaultValue(EmailDeliveryStatus.Pending);

        builder.Property(e => e.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.LastAttemptAt)
            .HasColumnName("last_attempt_at");

        builder.Property(e => e.SentAt)
            .HasColumnName("sent_at");

        builder.Property(e => e.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(500);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.LeaseOwnerToken)
            .HasColumnName("lease_owner_token");

        builder.Property(e => e.LeaseAcquiredAt)
            .HasColumnName("lease_acquired_at");

        builder.Property(e => e.LeaseExpiresAt)
            .HasColumnName("lease_expires_at");

        builder.HasIndex(e => e.AlertId).IsUnique();

        builder.HasIndex(e => e.Status);

        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasOne<AdministrativeAlert>()
            .WithMany()
            .HasForeignKey(e => e.AlertId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
