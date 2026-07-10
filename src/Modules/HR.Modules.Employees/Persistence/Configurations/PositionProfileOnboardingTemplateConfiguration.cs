using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class PositionProfileOnboardingTemplateConfiguration : IEntityTypeConfiguration<PositionProfileOnboardingTemplate>
{
    public void Configure(EntityTypeBuilder<PositionProfileOnboardingTemplate> builder)
    {
        builder.ToTable("position_profile_onboarding_templates");

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

        builder.Property(p => p.OnboardingTemplateId)
            .HasColumnName("onboarding_template_id")
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
        builder.HasIndex(p => p.OnboardingTemplateId);

        // Prevents assigning the same template to the same position profile twice while the
        // assignment is active. Scoped to active rows only (partial index) so a template can be
        // re-assigned after a prior assignment has been soft-removed.
        builder.HasIndex(p => new { p.CompanyId, p.PositionProfileId, p.OnboardingTemplateId })
            .IsUnique()
            .HasFilter("is_active");

        builder.HasOne<PositionProfile>()
            .WithMany()
            .HasForeignKey(p => p.PositionProfileId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
