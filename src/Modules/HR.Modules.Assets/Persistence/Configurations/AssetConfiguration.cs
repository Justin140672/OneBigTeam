using HR.Modules.Assets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Assets.Persistence.Configurations;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.AssetNumber)
            .HasColumnName("asset_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.Property(a => a.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.Manufacturer)
            .HasColumnName("manufacturer")
            .HasMaxLength(100);

        builder.Property(a => a.Model)
            .HasColumnName("model")
            .HasMaxLength(100);

        builder.Property(a => a.SerialNumber)
            .HasColumnName("serial_number")
            .HasMaxLength(100);

        builder.Property(a => a.PurchaseDate)
            .HasColumnName("purchase_date");

        builder.Property(a => a.PurchasePrice)
            .HasColumnName("purchase_price")
            .HasPrecision(18, 2);

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(a => a.CompanyId);
        builder.HasIndex(a => new { a.CompanyId, a.AssetNumber }).IsUnique();
        builder.HasIndex(a => new { a.CompanyId, a.Status });
    }
}
