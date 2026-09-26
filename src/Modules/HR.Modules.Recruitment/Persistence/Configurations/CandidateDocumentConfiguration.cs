using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class CandidateDocumentConfiguration : IEntityTypeConfiguration<CandidateDocument>
{
    public void Configure(EntityTypeBuilder<CandidateDocument> builder)
    {
        builder.ToTable("candidate_documents");

        builder.HasKey(cd => cd.Id);

        builder.Property(cd => cd.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(cd => cd.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(cd => cd.CandidateId)
            .HasColumnName("candidate_id")
            .IsRequired();

        builder.Property(cd => cd.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(cd => cd.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Domain.CandidateDocumentKind.Other)
            .IsRequired();

        builder.Property(cd => cd.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(cd => cd.FileSize)
            .HasColumnName("file_size")
            .IsRequired();

        builder.Property(cd => cd.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(cd => cd.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(cd => cd.UploadedBy)
            .HasColumnName("uploaded_by")
            .IsRequired();

        builder.Property(cd => cd.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // [P1] Candidate CV malware scanning. The database default is Pending so any row inserted
        // without an explicit status — and every row that existed before this column was added — is
        // treated as unscanned (never downloadable) until ScanCandidateDocumentJob records a result.
        builder.Property(cd => cd.ScanStatus)
            .HasColumnName("scan_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(CandidateDocumentScanStatus.Pending)
            .IsRequired();

        // Doubles as the optimistic-concurrency token for scan claims: every claim increments it, so
        // two workers racing to claim the same document cannot both succeed.
        builder.Property(cd => cd.ScanAttemptCount)
            .HasColumnName("scan_attempt_count")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(cd => cd.ScanLastAttemptAt)
            .HasColumnName("scan_last_attempt_at");

        builder.Property(cd => cd.ScanNextAttemptAt)
            .HasColumnName("scan_next_attempt_at");

        builder.Property(cd => cd.ScanCompletedAt)
            .HasColumnName("scan_completed_at");

        // Only closed-set categories or a sanitised threat name — never raw exception text.
        builder.Property(cd => cd.ScanFailureReason)
            .HasColumnName("scan_failure_reason")
            .HasMaxLength(200);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_candidate_documents_scan_status",
                "scan_status IN ('Pending', 'Scanning', 'Clean', 'Infected', 'Failed')");
            t.HasCheckConstraint(
                "ck_candidate_documents_scan_attempt_count",
                "scan_attempt_count >= 0");
        });

        builder.HasOne<Candidate>()
            .WithMany()
            .HasForeignKey(cd => cd.CandidateId)
            .OnDelete(DeleteBehavior.Restrict);

        // Internal recruitment Ticket 1: principal key for applications.cv_document_id's composite FK
        // (see ApplicationConfiguration). Trivially unique because id is the primary key; it exists so
        // the FK can also pin candidate_id and company_id.
        builder.HasAlternateKey(cd => new { cd.Id, cd.CandidateId, cd.CompanyId })
            .HasName("ak_candidate_documents_id_candidate_id_company_id");

        builder.HasIndex(cd => cd.CompanyId);
        builder.HasIndex(cd => cd.CandidateId);
        builder.HasIndex(cd => new { cd.CandidateId, cd.Kind });

        // Drives ReconcileCandidateDocumentScansJob's cross-company sweep for Pending/Scanning rows.
        builder.HasIndex(cd => new { cd.ScanStatus, cd.CreatedAt })
            .HasDatabaseName("ix_candidate_documents_scan_status_created_at");
    }
}
