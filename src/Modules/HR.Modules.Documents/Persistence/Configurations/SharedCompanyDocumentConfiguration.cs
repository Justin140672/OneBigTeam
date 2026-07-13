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
