using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ToTable("document_types");

        builder.HasKey(dt => dt.Id);

        builder.Property(dt => dt.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(dt => dt.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(dt => dt.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(dt => dt.Description)
            .HasColumnName("description")
            .HasMaxLength(1000);

        builder.Property(dt => dt.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(dt => dt.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(dt => dt.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(dt => dt.CompanyId);
        builder.HasIndex(dt => new { dt.CompanyId, dt.Name }).IsUnique();
    }
}
