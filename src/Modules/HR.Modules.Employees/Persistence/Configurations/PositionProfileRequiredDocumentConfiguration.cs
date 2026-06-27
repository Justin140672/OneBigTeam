using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class PositionProfileRequiredDocumentConfiguration : IEntityTypeConfiguration<PositionProfileRequiredDocument>
{
    public void Configure(EntityTypeBuilder<PositionProfileRequiredDocument> builder)
    {
        builder.ToTable("position_profile_required_documents");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(p => p.PositionProfileId)
            .HasColumnName("position_profile_id")
            .IsRequired();

        builder.Property(p => p.DocumentTypeId)
            .HasColumnName("document_type_id")
            .IsRequired();

        builder.Property(p => p.IsMandatory)
            .HasColumnName("is_mandatory")
            .IsRequired();

        builder.Property(p => p.DueDaysAfterStart)
            .HasColumnName("due_days_after_start");

        builder.Property(p => p.RequiresExpiryDate)
            .HasColumnName("requires_expiry_date")
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.HasIndex(p => p.CompanyId);
        builder.HasIndex(p => p.PositionProfileId);
        builder.HasIndex(p => p.DocumentTypeId);
    }
}
