using HR.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Notifications.Persistence.Configurations;

internal sealed class NotificationAuditReconciliationCursorConfiguration
    : IEntityTypeConfiguration<NotificationAuditReconciliationCursor>
{
    public void Configure(EntityTypeBuilder<NotificationAuditReconciliationCursor> builder)
    {
        builder.ToTable("notification_audit_reconciliation_cursors");

        builder.HasKey(c => c.CompanyId);

        builder.Property(c => c.CompanyId)
            .HasColumnName("company_id")
            .ValueGeneratedNever();

        builder.Property(c => c.LastScannedCreatedAt)
            .HasColumnName("last_scanned_created_at")
            .IsRequired();

        builder.Property(c => c.LastScannedNotificationId)
            .HasColumnName("last_scanned_notification_id")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
