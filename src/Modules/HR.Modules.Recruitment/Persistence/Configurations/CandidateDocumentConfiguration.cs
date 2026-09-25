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
    }
}
