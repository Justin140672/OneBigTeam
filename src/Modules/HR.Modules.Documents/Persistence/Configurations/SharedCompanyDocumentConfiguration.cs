using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class SharedCompanyDocumentConfiguration : IEntityTypeConfiguration<SharedCompanyDocument>
{
    public void Configure(EntityTypeBuilder<SharedCompanyDocument> builder)
    {
        builder.ToTable("shared_company_documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(d => d.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(d => d.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(d => d.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        builder.Property(d => d.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.Property(d => d.CurrentFileReference)
            .HasColumnName("current_file_reference")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(d => d.FileSize)
            .HasColumnName("file_size")
            .IsRequired();

        builder.Property(d => d.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.VersionNumber)
            .HasColumnName("version_number")
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.EffectiveDate)
            .HasColumnName("effective_date");

        builder.Property(d => d.ReviewDate)
            .HasColumnName("review_date");

        builder.Property(d => d.ReviewFrequency)
            .HasColumnName("review_frequency")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.CustomReviewFrequencyMonths)
            .HasColumnName("custom_review_frequency_months");

        // Plain Guid column, no FK — Employee lives in the Employees module, resolved only via
        // IEmployeeNameReader/IEmployeeAudienceReader at read time, same as
        // SharedCompanyDocumentAudienceRule.TargetId.
        builder.Property(d => d.ReviewOwnerEmployeeId)
            .HasColumnName("review_owner_employee_id");

        builder.Property(d => d.LastReviewedAt)
            .HasColumnName("last_reviewed_at");

        builder.Property(d => d.LastReviewedByEmployeeId)
            .HasColumnName("last_reviewed_by_employee_id");

        builder.Property(d => d.LastReviewNotes)
            .HasColumnName("last_review_notes")
            .HasMaxLength(2000);

        builder.Property(d => d.RequiresAcknowledgement)
            .HasColumnName("requires_acknowledgement")
            .IsRequired();

        builder.Property(d => d.AcknowledgementDueDate)
            .HasColumnName("acknowledgement_due_date");

        builder.Property(d => d.AcknowledgementStatement)
            .HasColumnName("acknowledgement_statement")
            .HasMaxLength(1000);

        builder.Property(d => d.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(d => d.UpdatedBy)
            .HasColumnName("updated_by")
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(d => d.PublishedBy)
            .HasColumnName("published_by");

        builder.Property(d => d.PublishedAt)
            .HasColumnName("published_at");

        builder.Property(d => d.ArchivedBy)
            .HasColumnName("archived_by");

        builder.Property(d => d.ArchivedAt)
            .HasColumnName("archived_at");

        builder.Property(d => d.ArchiveReason)
            .HasColumnName("archive_reason")
            .HasMaxLength(500);

        builder.Property(d => d.ExpiredBy)
            .HasColumnName("expired_by");

        builder.Property(d => d.ExpiredAt)
            .HasColumnName("expired_at");

        builder.Property(d => d.ScanStatus)
            .HasColumnName("scan_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.ScanCompletedAt)
            .HasColumnName("scan_completed_at");

        builder.Property(d => d.ScanAttemptCount)
            .HasColumnName("scan_attempt_count")
            .IsRequired();

        builder.Property(d => d.ScanFailureReason)
            .HasColumnName("scan_failure_reason")
            .HasMaxLength(500);

        builder.HasOne<CompanyDocumentCategory>()
            .WithMany()
            .HasForeignKey(d => d.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Every query against this table must filter by CompanyId (no EF global query filter
        // is used in this codebase — see the tenant-isolation note on the entity itself).
        builder.HasIndex(d => d.CompanyId);
        builder.HasIndex(d => new { d.CompanyId, d.Status });
        builder.HasIndex(d => new { d.CompanyId, d.CategoryId });
    }
}
