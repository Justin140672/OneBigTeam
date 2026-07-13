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

        builder.HasOne<SharedCompanyDocument>()
            .WithMany()
            .HasForeignKey(v => v.SharedCompanyDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => v.CompanyId);
        builder.HasIndex(v => new { v.SharedCompanyDocumentId, v.VersionNumber }).IsUnique();
    }
}
