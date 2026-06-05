using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class CompanyAddressConfiguration : IEntityTypeConfiguration<CompanyAddress>
{
    public void Configure(EntityTypeBuilder<CompanyAddress> builder)
    {
        builder.ToTable("company_addresses");

        builder.HasKey(address => address.Id);

        builder.Property(address => address.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(address => address.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(address => address.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(address => address.Line1)
            .HasColumnName("line1")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(address => address.Line2)
            .HasColumnName("line2")
            .HasMaxLength(200);

        builder.Property(address => address.City)
            .HasColumnName("city")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(address => address.Region)
            .HasColumnName("region")
            .HasMaxLength(100);

        builder.Property(address => address.PostalCode)
            .HasColumnName("postal_code")
            .HasMaxLength(20);

        builder.Property(address => address.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(address => address.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(address => address.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(address => new { address.CompanyId, address.Type })
            .IsUnique();
    }
}
