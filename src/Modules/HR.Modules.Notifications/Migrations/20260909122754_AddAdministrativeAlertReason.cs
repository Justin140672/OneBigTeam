using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeAlertReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "reason",
                schema: "notifications",
                table: "administrative_alerts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reason",
                schema: "notifications",
                table: "administrative_alerts");
        }
    }
}
