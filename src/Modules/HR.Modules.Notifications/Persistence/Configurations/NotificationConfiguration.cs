using HR.Modules.Notifications.Domain;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Notifications.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(n => n.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(n => n.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(n => n.Title)
            .HasColumnName("title")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(n => n.Body)
            .HasColumnName("body")
            .HasMaxLength(2000);

        builder.Property(n => n.IsRead)
            .HasColumnName("is_read")
            .IsRequired();

        builder.Property(n => n.SourceEntityId)
            .HasColumnName("source_entity_id")
            .IsRequired();

        builder.Property(n => n.Type)
            .HasColumnName("type")
            .IsRequired()
            .HasDefaultValue(NotificationType.TaskAssigned);

        builder.Property(n => n.Priority)
            .HasColumnName("priority")
            .IsRequired()
            .HasDefaultValue(NotificationPriority.Normal);

        builder.Property(n => n.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(n => new { n.CompanyId, n.EmployeeId, n.IsRead });

        builder.HasIndex(n => new { n.EmployeeId, n.SourceEntityId, n.Type }).IsUnique();
    }
}
