using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class SharedCompanyDocumentVersionConfiguration : IEntityTypeConfiguration<SharedCompanyDocumentVersion>
{
    public void Configure(EntityTypeBuilder<SharedCompanyDocumentVersion> builder)
    {
        builder.ToTable("shared_company_document_versions");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(v => v.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(v => v.SharedCompanyDocumentId)
            .HasColumnName("shared_company_document_id")
            .IsRequired();

        builder.Property(v => v.VersionNumber)
            .HasColumnName("version_number")
            .IsRequired();

        builder.Property(v => v.FileReference)
            .HasColumnName("file_reference")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(v => v.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(v => v.FileSize)
            .HasColumnName("file_size")
            .IsRequired();

        builder.Property(v => v.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(v => v.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(v => v.VersionNote)
            .HasColumnName("version_note")
            .HasMaxLength(1000);

        builder.Property(v => v.RequiresAcknowledgement)
            .HasColumnName("requires_acknowledgement")
            .IsRequired();

        builder.Property(v => v.EffectiveDate)
            .HasColumnName("effective_date");

        builder.Property(v => v.AcknowledgementStatement)
            .HasColumnName("acknowledgement_statement")
            .HasMaxLength(1000);

        builder.Property(v => v.ScanStatus)
            .HasColumnName("scan_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.ScanCompletedAt)
            .HasColumnName("scan_completed_at");

        builder.Property(v => v.ScanAttemptCount)
            .HasColumnName("scan_attempt_count")
            .IsRequired();

        builder.Property(v => v.ScanFailureReason)
            .HasColumnName("scan_failure_reason")
            .HasMaxLength(500);

        builder.HasOne<SharedCompanyDocument>()
            .WithMany()
            .HasForeignKey(v => v.SharedCompanyDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => v.CompanyId);
        builder.HasIndex(v => new { v.SharedCompanyDocumentId, v.VersionNumber }).IsUnique();
    }
}
