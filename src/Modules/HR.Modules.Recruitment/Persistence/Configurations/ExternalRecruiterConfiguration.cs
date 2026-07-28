using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class ExternalRecruiterConfiguration : IEntityTypeConfiguration<ExternalRecruiter>
{
    public void Configure(EntityTypeBuilder<ExternalRecruiter> builder)
    {
        builder.ToTable("external_recruiters");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.AgencyName)
            .HasColumnName("agency_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.ContactName)
            .HasColumnName("contact_name")
            .HasMaxLength(200);

        builder.Property(r => r.ContactEmail)
            .HasColumnName("contact_email")
            .HasMaxLength(320);

        builder.Property(r => r.ContactTelephone)
            .HasColumnName("contact_telephone")
            .HasMaxLength(50);

        builder.Property(r => r.Website)
            .HasColumnName("website")
            .HasMaxLength(500);

        builder.Property(r => r.Notes)
            .HasColumnName("notes")
            .HasMaxLength(4000);

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(r => r.CompanyId);
    }
}
