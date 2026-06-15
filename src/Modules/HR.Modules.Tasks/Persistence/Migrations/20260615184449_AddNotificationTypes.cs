using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "type",
                schema: "tasks",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_employee_id_source_entity_id_type",
                schema: "tasks",
                table: "notifications",
                columns: new[] { "employee_id", "source_entity_id", "type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_employee_id_source_entity_id_type",
                schema: "tasks",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "tasks",
                table: "notifications");
        }
    }
}
