using HR.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Support.Persistence.Configurations;

internal sealed class SupportNotificationAttemptConfiguration : IEntityTypeConfiguration<SupportNotificationAttempt>
{
    public void Configure(EntityTypeBuilder<SupportNotificationAttempt> builder)
    {
        builder.ToTable("support_notification_attempts");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(n => n.SupportRequestId)
            .HasColumnName("support_request_id")
            .IsRequired();

        builder.Property(n => n.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(n => n.NotificationType)
            .HasColumnName("notification_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(n => n.RecipientEmail)
            .HasColumnName("recipient_email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(n => n.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(n => n.AttemptedAt)
            .HasColumnName("attempted_at")
            .IsRequired();

        builder.Property(n => n.SentAt)
            .HasColumnName("sent_at");

        builder.Property(n => n.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2000);

        builder.Property(n => n.RetryCount)
            .HasColumnName("retry_count")
            .IsRequired();

        builder.HasIndex(n => n.CompanyId);
        builder.HasIndex(n => n.SupportRequestId);
        builder.HasIndex(n => n.Status);
    }
}
