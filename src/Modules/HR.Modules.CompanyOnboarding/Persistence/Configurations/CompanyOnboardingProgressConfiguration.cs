using HR.Modules.CompanyOnboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.CompanyOnboarding.Persistence.Configurations;

internal sealed class CompanyOnboardingProgressConfiguration : IEntityTypeConfiguration<CompanyOnboardingProgress>
{
    public void Configure(EntityTypeBuilder<CompanyOnboardingProgress> builder)
    {
        builder.ToTable("progress");

        builder.HasKey(p => p.CompanyId);

        builder.Property(p => p.CompanyId)
            .HasColumnName("company_id")
            .ValueGeneratedNever();

        builder.Property(p => p.IsDismissedEarly)
            .HasColumnName("is_dismissed_early")
            .IsRequired();

        builder.Property(p => p.IsHidden)
            .HasColumnName("is_hidden")
            .IsRequired();

        builder.Property(p => p.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
