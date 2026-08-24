using HR.Modules.Companies.Domain;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class CompanySettingsConfiguration : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> builder)
    {
        builder.ToTable("company_settings", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_company_settings_leave_year_start_month",
                "leave_year_start_month BETWEEN 1 AND 12");
            tableBuilder.HasCheckConstraint(
                "CK_company_settings_next_employee_number",
                "next_employee_number > 0");
            tableBuilder.HasCheckConstraint(
                "CK_company_settings_employee_number_minimum_length",
                "employee_number_minimum_length BETWEEN 1 AND 10");
            tableBuilder.HasCheckConstraint(
                "CK_company_settings_next_asset_number",
                "next_asset_number > 0");
            tableBuilder.HasCheckConstraint(
                "CK_company_settings_asset_number_minimum_length",
                "asset_number_minimum_length BETWEEN 1 AND 10");
        });

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
            .IsRequired()
            .HasDefaultValue(7);

        builder.Property(settings => settings.ReturnToWorkRequiredAfterDays)
            .HasColumnName("return_to_work_required_after_days")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(settings => settings.FrequentAbsenceCountThreshold)
            .HasColumnName("frequent_absence_count_threshold")
            .IsRequired()
            .HasDefaultValue(4);

        builder.Property(settings => settings.FrequentAbsenceWindowDays)
            .HasColumnName("frequent_absence_window_days")
            .IsRequired()
            .HasDefaultValue(365);

        builder.Property(settings => settings.LongAbsenceDayThreshold)
            .HasColumnName("long_absence_day_threshold")
            .IsRequired()
            .HasDefaultValue(28);

        builder.Property(settings => settings.WeekdayPatternOccurrenceThreshold)
            .HasColumnName("weekday_pattern_occurrence_threshold")
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(settings => settings.WeekdayPatternWindowDays)
            .HasColumnName("weekday_pattern_window_days")
            .IsRequired()
            .HasDefaultValue(365);

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

        builder.Property(settings => settings.AcknowledgementReminderIntervalDays)
            .HasColumnName("acknowledgement_reminder_interval_days")
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(settings => settings.NoticePeriodUnit)
            .HasColumnName("notice_period_unit")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(NoticePeriodUnit.Months);

        builder.Property(settings => settings.NoticePeriodLength)
            .HasColumnName("notice_period_length")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(settings => settings.AutoDisableAccessOnLeavingDate)
            .HasColumnName("auto_disable_access_on_leaving_date")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(settings => settings.EmployeeNumberMode)
            .HasColumnName("employee_number_mode")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(EmployeeNumberMode.Manual);

        builder.Property(settings => settings.EmployeeNumberPrefix)
            .HasColumnName("employee_number_prefix")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(settings => settings.NextEmployeeNumber)
            .HasColumnName("next_employee_number")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(settings => settings.EmployeeNumberMinimumLength)
            .HasColumnName("employee_number_minimum_length")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(settings => settings.AssetNumberMode)
            .HasColumnName("asset_number_mode")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(AssetNumberMode.Manual);

        builder.Property(settings => settings.AssetNumberPrefix)
            .HasColumnName("asset_number_prefix")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(settings => settings.NextAssetNumber)
            .HasColumnName("next_asset_number")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(settings => settings.AssetNumberMinimumLength)
            .HasColumnName("asset_number_minimum_length")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(settings => settings.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(settings => settings.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
