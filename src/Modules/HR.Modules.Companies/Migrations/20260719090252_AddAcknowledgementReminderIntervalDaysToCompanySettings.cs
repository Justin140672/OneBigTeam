using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddAcknowledgementReminderIntervalDaysToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "acknowledgement_reminder_interval_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acknowledgement_reminder_interval_days",
                schema: "companies",
                table: "company_settings");
        }
    }
}
