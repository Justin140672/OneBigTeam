using HR.Modules.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Onboarding.Persistence.Configurations;

internal sealed class OnboardingTaskTemplateConfiguration : IEntityTypeConfiguration<OnboardingTaskTemplate>
{
    public void Configure(EntityTypeBuilder<OnboardingTaskTemplate> builder)
    {
        builder.ToTable("onboarding_task_templates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(t => t.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        builder.Property(t => t.DefaultDueDayOffset)
            .HasColumnName("default_due_day_offset");

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(t => t.CompanyId);
    }
}
