using HR.Modules.Marketing.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Marketing.Persistence.Configurations;

internal sealed class MarketingFeatureConfiguration : IEntityTypeConfiguration<MarketingFeature>
{
    public void Configure(EntityTypeBuilder<MarketingFeature> builder)
    {
        builder.ToTable("marketing_features");

        builder.HasKey(f => f.Id);

        builder.Ignore(f => f.Benefits);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(f => f.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(f => f.Slug)
            .HasColumnName("slug")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(f => f.IconName)
            .HasColumnName("icon_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(f => f.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(f => f.Summary)
            .HasColumnName("summary")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(f => f.Intro)
            .HasColumnName("intro")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(f => f.DetailedContent)
            .HasColumnName("detailed_content")
            .HasColumnType("text");

        builder.Property(f => f.BenefitsJson)
            .HasColumnName("benefits_json")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("[]");

        builder.Property(f => f.YouTubeId)
            .HasColumnName("youtube_id")
            .HasMaxLength(32);

        builder.Property(f => f.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(f => f.IsPublished)
            .HasColumnName("is_published")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(f => f.DeliveryStatus)
            .HasColumnName("delivery_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(f => f.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(f => f.UpdatedByUserId)
            .HasColumnName("updated_by_user_id");

        builder.HasIndex(f => f.Slug).IsUnique();
        builder.HasIndex(f => f.ProductId);
        builder.HasIndex(f => new { f.IsPublished, f.DisplayOrder });
    }
}
