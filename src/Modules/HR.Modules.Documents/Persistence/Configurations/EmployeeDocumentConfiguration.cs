using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("employee_documents");

        builder.HasKey(ed => ed.Id);

        builder.Property(ed => ed.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(ed => ed.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(ed => ed.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(ed => ed.DocumentId)
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(ed => ed.AddedBy)
            .HasColumnName("added_by")
            .IsRequired();

        builder.Property(ed => ed.IssueDate)
            .HasColumnName("issue_date");

        builder.Property(ed => ed.ExpiryDate)
            .HasColumnName("expiry_date");

        builder.Property(ed => ed.AcknowledgedAt)
            .HasColumnName("acknowledged_at");

        builder.Property(ed => ed.ExpiringSoonNotifiedAt)
            .HasColumnName("expiring_soon_notified_at");

        builder.Property(ed => ed.ExpiredNotifiedAt)
            .HasColumnName("expired_notified_at");

        builder.Property(ed => ed.ExpiryReminder90SentAt)
            .HasColumnName("expiry_reminder_90_sent_at");

        builder.Property(ed => ed.ExpiryReminder30SentAt)
            .HasColumnName("expiry_reminder_30_sent_at");

        builder.Property(ed => ed.ExpiryReminder7SentAt)
            .HasColumnName("expiry_reminder_7_sent_at");

        builder.Property(ed => ed.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(ed => ed.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // DOC-04: recoverable soft-delete/archive state. IsArchived defaults to false so the
        // migration adding these columns is purely additive — every pre-existing row becomes
        // IsArchived = false with no other changes.
        builder.Property(ed => ed.IsArchived)
            .HasColumnName("is_archived")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(ed => ed.ArchivedByUserId)
            .HasColumnName("archived_by_user_id");

        builder.Property(ed => ed.ArchivedAt)
            .HasColumnName("archived_at");

        builder.Property(ed => ed.ArchiveReason)
            .HasColumnName("archive_reason")
            .HasMaxLength(1000);

        builder.Property(ed => ed.RestoredByUserId)
            .HasColumnName("restored_by_user_id");

        builder.Property(ed => ed.RestoredAt)
            .HasColumnName("restored_at");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(ed => ed.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ed => new { ed.CompanyId, ed.EmployeeId });
        builder.HasIndex(ed => new { ed.EmployeeId, ed.DocumentId }).IsUnique();
        builder.HasIndex(ed => new { ed.CompanyId, ed.IsArchived });
    }
}
