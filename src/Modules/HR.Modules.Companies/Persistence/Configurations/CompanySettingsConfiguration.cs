using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class CompanySettingsConfiguration : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> builder)
    {
        builder.ToTable("company_settings", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_company_settings_leave_year_start_month",
                "leave_year_start_month BETWEEN 1 AND 12"));

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

        builder.Property(settings => settings.WorkingDays)
            .HasColumnName("working_days")
            .IsRequired();

        builder.Property(settings => settings.HoursPerDay)
            .HasColumnName("hours_per_day")
            .HasPrecision(4, 2)
            .IsRequired();

        builder.Property(settings => settings.LeaveYearStartMonth)
            .HasColumnName("leave_year_start_month")
            .IsRequired();

        builder.Property(settings => settings.DefaultHolidayAllowance)
            .HasColumnName("default_holiday_allowance")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(settings => settings.ProbationMonths)
            .HasColumnName("probation_months")
            .IsRequired();

        builder.Property(settings => settings.ExcludePublicHolidaysFromLeave)
            .HasColumnName("exclude_public_holidays_from_leave")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(settings => settings.ExcludePublicHolidaysFromSickness)
            .HasColumnName("exclude_public_holidays_from_sickness")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(settings => settings.DisplaySalaryOnEmployeeProfile)
            .HasColumnName("display_salary_on_employee_profile")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(settings => settings.FitNoteRequiredAfterDays)
            .HasColumnName("fit_note_required_after_days")
            .IsRequired(false);

        builder.Property(settings => settings.ReturnToWorkRequiredAfterDays)
            .HasColumnName("return_to_work_required_after_days")
            .IsRequired(false);

        builder.Property(settings => settings.PostcodeRegex)
            .HasColumnName("postcode_regex")
            .HasMaxLength(500)
            .IsRequired()
            .HasDefaultValue(UkContactRegexDefaults.Postcode);

        builder.Property(settings => settings.TelephoneRegex)
            .HasColumnName("telephone_regex")
            .HasMaxLength(500)
            .IsRequired()
            .HasDefaultValue(UkContactRegexDefaults.Telephone);

        builder.Property(settings => settings.MobileRegex)
            .HasColumnName("mobile_regex")
            .HasMaxLength(500)
            .IsRequired()
            .HasDefaultValue(UkContactRegexDefaults.Mobile);

        builder.Property(settings => settings.DefaultAcknowledgementStatement)
            .HasColumnName("default_acknowledgement_statement")
            .HasMaxLength(2000)
            .IsRequired()
            .HasDefaultValue(CompanySettings.DefaultAcknowledgementStatementText);

        builder.Property(settings => settings.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(settings => settings.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
