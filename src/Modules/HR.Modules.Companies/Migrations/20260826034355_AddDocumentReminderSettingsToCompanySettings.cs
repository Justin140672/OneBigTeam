using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentReminderSettingsToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "document_reminder_offset_days_1",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                defaultValue: 90);

            migrationBuilder.AddColumn<int>(
                name: "document_reminder_offset_days_2",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "document_reminder_offset_days_3",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                defaultValue: 7);

            migrationBuilder.AddColumn<bool>(
                name: "document_reminders_enabled",
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
                name: "document_reminder_offset_days_1",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "document_reminder_offset_days_2",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "document_reminder_offset_days_3",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "document_reminders_enabled",
                schema: "companies",
                table: "company_settings");
        }
    }
}
