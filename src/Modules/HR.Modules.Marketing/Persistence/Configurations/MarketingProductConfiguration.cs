using HR.Modules.Marketing.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Marketing.Persistence.Configurations;

internal sealed class MarketingProductConfiguration : IEntityTypeConfiguration<MarketingProduct>
{
    public void Configure(EntityTypeBuilder<MarketingProduct> builder)
    {
        builder.ToTable("marketing_products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Tagline)
            .HasColumnName("tagline")
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(p => p.UpdatedByUserId)
            .HasColumnName("updated_by_user_id");
    }
}
