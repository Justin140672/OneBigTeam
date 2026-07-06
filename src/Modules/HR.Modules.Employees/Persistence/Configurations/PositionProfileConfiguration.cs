using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class PositionProfileConfiguration : IEntityTypeConfiguration<PositionProfile>
{
    public void Configure(EntityTypeBuilder<PositionProfile> builder)
    {
        builder.ToTable("position_profiles");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(p => p.DepartmentId)
            .HasColumnName("department_id");

        builder.Property(p => p.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        builder.Property(p => p.IsManagerial)
            .HasColumnName("is_managerial")
            .IsRequired();

        builder.Property(p => p.ProbationMonthsOverride)
            .HasColumnName("probation_months_override");

        builder.Property(p => p.WorkingDaysOverride)
            .HasColumnName("working_days_override");

        builder.Property(p => p.HoursPerDayOverride)
            .HasColumnName("hours_per_day_override")
            .HasPrecision(4, 2);

        builder.Property(p => p.SalaryMin)
            .HasColumnName("salary_min")
            .HasPrecision(12, 2);

        builder.Property(p => p.SalaryMax)
            .HasColumnName("salary_max")
            .HasPrecision(12, 2);

        builder.Property(p => p.SalaryType)
            .HasColumnName("salary_type")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.DefaultLeavePolicyId)
            .HasColumnName("default_leave_policy_id");

        builder.Property(p => p.OnboardingTemplateId)
            .HasColumnName("onboarding_template_id");

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(p => p.CompanyId);
        builder.HasIndex(p => p.DepartmentId);

        builder.HasMany(p => p.RequiredDocuments)
            .WithOne()
            .HasForeignKey(d => d.PositionProfileId)
            .IsRequired();

        builder.Navigation(p => p.RequiredDocuments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.RequiredAssets)
            .WithOne()
            .HasForeignKey(a => a.PositionProfileId)
            .IsRequired();

        builder.Navigation(p => p.RequiredAssets)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
