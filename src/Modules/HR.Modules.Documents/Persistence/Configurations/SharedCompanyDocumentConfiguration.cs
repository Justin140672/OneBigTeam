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

        builder.Property(d => d.Category)
            .HasColumnName("category")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.CurrentFileReference)
            .HasColumnName("current_file_reference")
            .HasMaxLength(500)
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

        builder.Property(d => d.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Every query against this table must filter by CompanyId (no EF global query filter
        // is used in this codebase — see the tenant-isolation note on the entity itself).
        builder.HasIndex(d => d.CompanyId);
        builder.HasIndex(d => new { d.CompanyId, d.Status });
        builder.HasIndex(d => new { d.CompanyId, d.Category });
    }
}
