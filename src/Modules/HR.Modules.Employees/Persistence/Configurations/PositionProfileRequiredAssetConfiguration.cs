using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class PositionProfileRequiredAssetConfiguration : IEntityTypeConfiguration<PositionProfileRequiredAsset>
{
    public void Configure(EntityTypeBuilder<PositionProfileRequiredAsset> builder)
    {
        builder.ToTable("position_profile_required_assets");

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

        builder.Property(p => p.AssetCategoryId)
            .HasColumnName("asset_category_id")
            .IsRequired();

        builder.Property(p => p.IsMandatory)
            .HasColumnName("is_mandatory")
            .IsRequired();

        builder.Property(p => p.Quantity)
            .HasColumnName("quantity")
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
        builder.HasIndex(p => p.AssetCategoryId);
    }
}
