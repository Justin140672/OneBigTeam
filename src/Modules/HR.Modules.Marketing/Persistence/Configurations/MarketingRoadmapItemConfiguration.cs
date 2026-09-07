using HR.Modules.Marketing.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Marketing.Persistence.Configurations;

internal sealed class MarketingRoadmapItemConfiguration : IEntityTypeConfiguration<MarketingRoadmapItem>
{
    public void Configure(EntityTypeBuilder<MarketingRoadmapItem> builder)
    {
        builder.ToTable("marketing_roadmap_items");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(r => r.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.IconName)
            .HasColumnName("icon_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.DeliveryStatus)
            .HasColumnName("delivery_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(r => r.IsPublished)
            .HasColumnName("is_published")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(r => r.UpdatedByUserId)
            .HasColumnName("updated_by_user_id");

        builder.HasIndex(r => r.ProductId);
        builder.HasIndex(r => new { r.IsPublished, r.DisplayOrder });
    }
}
