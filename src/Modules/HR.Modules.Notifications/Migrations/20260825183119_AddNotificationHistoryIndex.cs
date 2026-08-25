using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationHistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_company_id_employee_id_is_read",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.AddColumn<string>(
                name: "action_url",
                schema: "notifications",
                table: "notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_company_id_employee_id_is_read_created_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "company_id", "employee_id", "is_read", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_company_id_employee_id_is_read_created_at",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "action_url",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_company_id_employee_id_is_read",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "company_id", "employee_id", "is_read" });
        }
    }
}
