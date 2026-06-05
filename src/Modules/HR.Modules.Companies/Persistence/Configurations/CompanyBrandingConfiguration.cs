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

        builder.HasKey(b => b.CompanyId);

        builder.Property(b => b.CompanyId)
            .HasColumnName("company_id")
            .ValueGeneratedNever();

        builder.Property(b => b.PrimaryLogoUrl)
            .HasColumnName("primary_logo_url")
            .HasMaxLength(2048);

        builder.Property(b => b.SmallLogoUrl)
            .HasColumnName("small_logo_url")
            .HasMaxLength(2048);

        builder.Property(b => b.EmailLogoUrl)
            .HasColumnName("email_logo_url")
            .HasMaxLength(2048);

        builder.Property(b => b.PrimaryColor)
            .HasColumnName("primary_color")
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(b => b.SecondaryColor)
            .HasColumnName("secondary_color")
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(b => b.AccentColor)
            .HasColumnName("accent_color")
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
