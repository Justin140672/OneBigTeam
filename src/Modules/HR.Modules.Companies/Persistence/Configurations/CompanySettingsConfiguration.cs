using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class CompanySettingsConfiguration : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> builder)
    {
        builder.ToTable("company_settings");

        builder.HasKey(settings => settings.CompanyId);

        builder.Property(settings => settings.CompanyId)
            .HasColumnName("company_id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.TimeZone)
            .HasColumnName("time_zone")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(settings => settings.Locale)
            .HasColumnName("locale")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(settings => settings.WorkingWeek)
            .HasColumnName("working_week")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(settings => settings.LeaveYearStart)
            .HasColumnName("leave_year_start")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(settings => settings.DefaultHolidayAllowance)
            .HasColumnName("default_holiday_allowance")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(settings => settings.ProbationMonths)
            .HasColumnName("probation_months")
            .IsRequired();

        builder.Property(settings => settings.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(settings => settings.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
