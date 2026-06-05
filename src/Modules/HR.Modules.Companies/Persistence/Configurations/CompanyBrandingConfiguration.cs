using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class CompanyBrandingConfiguration : IEntityTypeConfiguration<CompanyBranding>
{
    public void Configure(EntityTypeBuilder<CompanyBranding> builder)
    {
        builder.ToTable("company_branding", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_company_branding_primary_color",
                "primary_color ~ '^#[0-9A-Fa-f]{6}$'");
            tableBuilder.HasCheckConstraint(
                "CK_company_branding_secondary_color",
                "secondary_color ~ '^#[0-9A-Fa-f]{6}$'");
            tableBuilder.HasCheckConstraint(
                "CK_company_branding_accent_color",
                "accent_color ~ '^#[0-9A-Fa-f]{6}$'");
        });

        builder.HasKey(branding => branding.CompanyId);

        builder.Property(branding => branding.CompanyId)
            .HasColumnName("company_id")
            .ValueGeneratedNever();

        builder.Property(branding => branding.PrimaryLogoUrl)
            .HasColumnName("primary_logo_url")
            .HasMaxLength(2048);

        builder.Property(branding => branding.SmallLogoUrl)
            .HasColumnName("small_logo_url")
            .HasMaxLength(2048);

        builder.Property(branding => branding.EmailLogoUrl)
            .HasColumnName("email_logo_url")
            .HasMaxLength(2048);

        builder.Property(branding => branding.PrimaryColor)
            .HasColumnName("primary_color")
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(branding => branding.SecondaryColor)
            .HasColumnName("secondary_color")
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(branding => branding.AccentColor)
            .HasColumnName("accent_color")
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(branding => branding.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(branding => branding.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}