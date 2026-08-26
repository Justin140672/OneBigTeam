using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSettingsToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "email_notifications_enabled",
                schema: "companies",
                table: "company_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "scheduled_reminders_enabled",
                schema: "companies",
                table: "company_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_notifications_enabled",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "scheduled_reminders_enabled",
                schema: "companies",
                table: "company_settings");
        }
    }
}
