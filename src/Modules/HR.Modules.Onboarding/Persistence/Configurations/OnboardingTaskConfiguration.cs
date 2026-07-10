using HR.Modules.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Onboarding.Persistence.Configurations;

internal sealed class OnboardingTaskConfiguration : IEntityTypeConfiguration<OnboardingTask>
{
    public void Configure(EntityTypeBuilder<OnboardingTask> builder)
    {
        builder.ToTable("onboarding_tasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(t => t.OnboardingPlanId)
            .HasColumnName("onboarding_plan_id")
            .IsRequired();

        builder.Property(t => t.Title)
            .HasColumnName("title")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        builder.Property(t => t.AssignTo)
            .HasColumnName("assign_to")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.DueDate)
            .HasColumnName("due_date");

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<OnboardingPlan>()
            .WithMany()
            .HasForeignKey(t => t.OnboardingPlanId);

        builder.HasIndex(t => t.OnboardingPlanId);
        builder.HasIndex(t => new { t.CompanyId, t.OnboardingPlanId });
    }
}
