using HR.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Notifications.Persistence.Configurations;

internal sealed class EmailDeliveryConfiguration : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> builder)
    {
        builder.ToTable("email_deliveries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.NotificationId)
            .HasColumnName("notification_id")
            .IsRequired();

        builder.Property(e => e.IdempotencyKey)
            .HasColumnName("idempotency_key")
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

        builder.Property(e => e.TemplateVersion)
            .HasColumnName("template_version");

        builder.Property(e => e.EmailSubject)
            .HasColumnName("email_subject")
            .HasMaxLength(500);

        builder.Property(e => e.EmailBody)
            .HasColumnName("email_body");

        builder.HasIndex(e => e.NotificationId).IsUnique();
        builder.HasIndex(e => e.IdempotencyKey).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.Status });

        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(e => e.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
