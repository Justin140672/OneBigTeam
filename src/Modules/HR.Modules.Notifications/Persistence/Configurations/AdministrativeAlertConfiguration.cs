using HR.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Notifications.Persistence.Configurations;

internal sealed class AdministrativeAlertConfiguration : IEntityTypeConfiguration<AdministrativeAlert>
{
    public void Configure(EntityTypeBuilder<AdministrativeAlert> builder)
    {
        builder.ToTable("administrative_alerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.Severity)
            .HasColumnName("severity")
            .IsRequired();

        builder.Property(a => a.Category)
            .HasColumnName("category")
            .IsRequired();

        builder.Property(a => a.Summary)
            .HasColumnName("summary")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(a => a.Detail)
            .HasColumnName("detail")
            .HasMaxLength(2000);

        builder.Property(a => a.DedupKey)
            .HasColumnName("dedup_key")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.OccurrenceCount)
            .HasColumnName("occurrence_count")
            .IsRequired();

        builder.Property(a => a.FirstOccurredAt)
            .HasColumnName("first_occurred_at")
            .IsRequired();

        builder.Property(a => a.LastOccurredAt)
            .HasColumnName("last_occurred_at")
            .IsRequired();

        builder.Property(a => a.AffectedEntityType)
            .HasColumnName("affected_entity_type")
            .HasMaxLength(100);

        builder.Property(a => a.AffectedEntityId)
            .HasColumnName("affected_entity_id");

        builder.Property(a => a.RecommendedAction)
            .HasColumnName("recommended_action")
            .HasMaxLength(500);

        builder.Property(a => a.ActionUrl)
            .HasColumnName("action_url")
            .HasMaxLength(500);

        builder.Property(a => a.IsRead)
            .HasColumnName("is_read")
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(a => a.AcknowledgedAt)
            .HasColumnName("acknowledged_at");

        builder.Property(a => a.AcknowledgedByUserId)
            .HasColumnName("acknowledged_by_user_id");

        builder.Property(a => a.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Property(a => a.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id");

        builder.Property(a => a.ResolutionNote)
            .HasColumnName("resolution_note")
            .HasMaxLength(1000);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(a => new { a.CompanyId, a.Status, a.Severity, a.LastOccurredAt });
        builder.HasIndex(a => new { a.CompanyId, a.IsRead });

        // ADM-03: one live alert per dedup key per company; resolved (status = 3) alerts drop out
        // so an identical failure after resolution starts a fresh alert.
        builder.HasIndex(a => new { a.CompanyId, a.DedupKey })
            .IsUnique()
            .HasFilter("status <> 3");
    }
}
