using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAlertSettingsToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "frequent_absence_count_threshold",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "frequent_absence_window_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 365);

            migrationBuilder.AddColumn<int>(
                name: "long_absence_day_threshold",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 28);

            migrationBuilder.AddColumn<int>(
                name: "weekday_pattern_occurrence_threshold",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "weekday_pattern_window_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 365);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "frequent_absence_count_threshold",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "frequent_absence_window_days",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "long_absence_day_threshold",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "weekday_pattern_occurrence_threshold",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "weekday_pattern_window_days",
                schema: "companies",
                table: "company_settings");
        }
    }
}
