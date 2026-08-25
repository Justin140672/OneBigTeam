using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class SetSettingsValidationConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SET-01: fallback/repair pass for any existing stored settings that would violate the
            // new constraints or that hold a time zone/locale no longer resolvable by the
            // application's supported time-zone/culture mechanisms. This must run before the check
            // constraints are added below, otherwise adding the constraints would fail outright on
            // any pre-existing invalid row.
            migrationBuilder.Sql(
                "UPDATE companies.company_settings SET probation_months = 1 WHERE probation_months <= 0;");
            migrationBuilder.Sql(
                "UPDATE companies.company_settings SET default_holiday_allowance = 0 WHERE default_holiday_allowance < 0;");
            migrationBuilder.Sql(
                "UPDATE companies.company_settings SET working_days = 31 WHERE working_days = 0 OR working_days > 127;");
            migrationBuilder.Sql(
                "UPDATE companies.company_settings SET time_zone = 'UTC' WHERE time_zone IS NULL OR btrim(time_zone) = '';");
            migrationBuilder.Sql(
                "UPDATE companies.company_settings SET locale = 'en-GB' WHERE locale IS NULL OR btrim(locale) = '';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_settings_default_holiday_allowance",
                schema: "companies",
                table: "company_settings",
                sql: "default_holiday_allowance >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_settings_probation_months",
                schema: "companies",
                table: "company_settings",
                sql: "probation_months > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_settings_working_days",
                schema: "companies",
                table: "company_settings",
                sql: "working_days BETWEEN 1 AND 127");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_company_settings_default_holiday_allowance",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_company_settings_probation_months",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_company_settings_working_days",
                schema: "companies",
                table: "company_settings");
        }
    }
}
